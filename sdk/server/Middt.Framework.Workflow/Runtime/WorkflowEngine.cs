using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Schema;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Executes workflow instances. Every command claims the instance (Active → Busy with a conditional UPDATE, so concurrent
/// requests get 409), runs the transition pipeline in memory and persists state, data snapshot, history and timers in one
/// transaction:
/// <c>input → onExecute → onExit → change state → onEntry → (finish | subflow | arm timers) → automatic transitions …</c>
/// </summary>
public sealed partial class WorkflowEngine(
    WorkflowDbContext db,
    WorkflowRegistry registry,
    WorkflowTaskRegistry tasks,
    WorkflowExpressionEngine expressions,
    IServiceProvider services,
    IServiceScopeFactory scopeFactory,
    IOptions<MiddtWorkflowOptions> options,
    TimeProvider time,
    WorkflowJobSignal jobSignal,
    ILogger<WorkflowEngine> logger)
{
    internal const string TimeoutTransition = "$timeout";
    internal const string ResumeTransition = "$subflow-completed";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ───────────────────────────── commands ─────────────────────────────

    /// <summary>Starts an instance; with an existing business key the existing instance is returned (<c>Created = false</c>).</summary>
    public Task<(WorkflowInstanceModel Instance, bool Created)> StartAsync(
        string workflow,
        StartWorkflowRequest request,
        WorkflowUser user,
        CancellationToken cancellationToken = default) =>
        StartAsync(workflow, request, user, null, cancellationToken);

    internal async Task<(WorkflowInstanceModel Instance, bool Created)> StartAsync(
        string workflow,
        StartWorkflowRequest request,
        WorkflowUser user,
        Guid? parentInstanceId,
        CancellationToken cancellationToken)
    {
        var definition = registry.GetLatest(workflow);
        EnsureCanRead(definition, user);

        if (!string.IsNullOrEmpty(request.Key) &&
            await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.WorkflowKey == workflow && i.BusinessKey == request.Key, cancellationToken) is { } existing)
        {
            return (await ToModelAsync(existing, user, cancellationToken), false);
        }

        ValidateInput(definition.Start, request.Data);

        var instance = new WorkflowInstance
        {
            Id = db.NewId(),
            WorkflowKey = definition.Key,
            WorkflowVersion = definition.Version,
            BusinessKey = string.IsNullOrEmpty(request.Key) ? null : request.Key,
            Status = WorkflowInstanceStatus.Busy,
            BusySince = Now,
            ParentInstanceId = parentInstanceId,
            StartedBy = user.AuditName,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        // Insert first: claims the business key and makes the instance visible (Busy) while tasks run.
        db.Instances.Add(instance);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (instance.BusinessKey is not null)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Instances.AsNoTracking().FirstAsync(i => i.WorkflowKey == workflow && i.BusinessKey == instance.BusinessKey, cancellationToken);
            return (await ToModelAsync(winner, user, cancellationToken), false);
        }

        // Children started by the engine (subflow / startWorkflow) receive their data directly; external callers
        // can only write data through a declared start input.
        var initialData = parentInstanceId is not null && definition.Start.Input is null && request.Data is not null
            ? (JsonObject)request.Data.DeepClone()
            : [];
        var run = new Run(definition, instance, initialData, user);
        run.Events.Add(Event(run, WorkflowEventType.Started, null, null));
        if (definition.Timeout is { } timeout)
        {
            run.NewJobs.Add(NewJob(instance, WorkflowJobKind.Timeout, TimeoutTransition, null, Now + Durations.Parse(timeout.After)));
        }

        try
        {
            await ExecuteChainAsync(run, definition.Start, request.Data, "Start", cancellationToken);
            await SaveAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            // Same guarantee as RunClaimedAsync: never leave the new instance Busy; it can be retried from the start transition.
            LogUnexpected(logger, ex, instance.Id);
            var pendingInput = definition.Start.Input?.Persistable(request.Data)?.ToJsonString() ?? (definition.Start.Input is null ? request.Data?.ToJsonString() : null);
            await db.Instances.Where(i => i.Id == instance.Id && i.Status == WorkflowInstanceStatus.Busy)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, WorkflowInstanceStatus.Faulted)
                    .SetProperty(i => i.BusySince, (DateTime?)null)
                    .SetProperty(i => i.PendingTransition, definition.Start.Key)
                    .SetProperty(i => i.PendingInput, pendingInput)
                    .SetProperty(i => i.Error, ex.Message), CancellationToken.None);
            throw;
        }

        return (await ToModelAsync(run, cancellationToken), true);
    }

    /// <summary>Executes a manual transition (synchronously, or queued when <paramref name="runAsync"/> is set → HTTP 202).</summary>
    public Task<WorkflowInstanceModel> ExecuteTransitionAsync(
        Guid instanceId,
        string transitionKey,
        JsonObject? input,
        WorkflowUser user,
        bool runAsync = false,
        CancellationToken cancellationToken = default) =>
        ExecuteTransitionAsync(null, instanceId, transitionKey, input, user, runAsync, cancellationToken);

    /// <param name="workflow">When set, the instance must belong to this workflow (otherwise 404).</param>
    internal async Task<WorkflowInstanceModel> ExecuteTransitionAsync(
        string? workflow,
        Guid instanceId,
        string transitionKey,
        JsonObject? input,
        WorkflowUser user,
        bool runAsync,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        var definition = registry.Get(snapshot.WorkflowKey, snapshot.WorkflowVersion);

        var transition = FindManualTransition(definition, snapshot, transitionKey, user);
        ValidateInput(transition, input);

        if (runAsync)
        {
            EnsureActive(snapshot);
            if (transition.Input?.Sensitive.Count > 0)
            {
                throw new WorkflowValidationException(["Transitions with sensitive input cannot run asynchronously."]);
            }

            db.Jobs.Add(NewJob(snapshot, WorkflowJobKind.Transition, transitionKey, snapshot.State, Now, input?.ToJsonString(), user.AuditName));
            await db.SaveChangesAsync(cancellationToken);
            jobSignal.Notify();
            return await ToModelAsync(snapshot, user, cancellationToken);
        }

        var run = await RunClaimedAsync(instanceId, WorkflowInstanceStatus.Active, user, async run =>
        {
            await ExecuteChainAsync(run, transition, input, nameof(TriggerType.Manual), cancellationToken);
            if (run.Instance.WaitingForInstanceId is { } child && run.Definition.FindState(run.Instance.State)?.IsFinal == true)
            {
                run.CancelChildren.Add(child);
                run.Instance.WaitingForInstanceId = null;
            }
        }, cancellationToken, (transition.Key, transition.Input?.Persistable(input) ?? input), snapshot);

        return await ToModelAsync(run, cancellationToken);
    }

    /// <summary>Re-runs the failed transition of a Faulted instance with its original input.</summary>
    public Task<WorkflowInstanceModel> RetryAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        RetryAsync(null, instanceId, user, cancellationToken);

    internal async Task<WorkflowInstanceModel> RetryAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        var snapshot = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);

        if (snapshot.Status != WorkflowInstanceStatus.Faulted || snapshot.PendingTransition is null)
        {
            throw new WorkflowConflictException("Only faulted instances can be retried.");
        }

        var run = await RunClaimedAsync(instanceId, WorkflowInstanceStatus.Faulted, user, async run =>
        {
            var transition = ResolveTransition(run.Definition, run.Instance, run.Instance.PendingTransition!)
                ?? throw new WorkflowConflictException($"Transition '{run.Instance.PendingTransition}' is no longer available.");
            var input = JsonPath.ParseObject(run.Instance.PendingInput);
            run.Instance.PendingTransition = null;
            run.Instance.PendingInput = null;
            run.Instance.Error = null;
            run.ResolveIncidents = true;
            await ExecuteChainAsync(run, transition, input.Count == 0 ? null : input, "Retry", cancellationToken);
        }, cancellationToken, (snapshot.PendingTransition, JsonPath.ParseObject(snapshot.PendingInput) is { Count: > 0 } retryInput ? retryInput : null), snapshot);

        return await ToModelAsync(run, cancellationToken);
    }

    /// <summary>Executes a due job (called by <see cref="WorkflowJobProcessor"/>).</summary>
    internal async Task<JobOutcome> ProcessJobAsync(WorkflowJob job, CancellationToken cancellationToken)
    {
        var instance = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == job.InstanceId, cancellationToken);
        if (instance is null || instance.IsTerminal)
        {
            return JobOutcome.Done;
        }

        // Timers of a faulted instance wait until it is retried (checked again later, without consuming attempts).
        if (instance.Status == WorkflowInstanceStatus.Faulted && job.Kind != WorkflowJobKind.ResumeParent)
        {
            return JobOutcome.Postpone;
        }

        var actor = new WorkflowUser { Name = job.RequestedBy ?? "system", IsSystem = true };
        try
        {
            switch (job.Kind)
            {
                case WorkflowJobKind.Scheduled when instance.Status == WorkflowInstanceStatus.Active && instance.State == job.State:
                    await RunClaimedAsync(instance.Id, WorkflowInstanceStatus.Active, actor, run =>
                        ExecuteChainAsync(run, ResolveTransition(run.Definition, run.Instance, job.TransitionKey!)!, null, nameof(TriggerType.Scheduled), cancellationToken), cancellationToken, (job.TransitionKey!, null));
                    return JobOutcome.Done;

                case WorkflowJobKind.Scheduled:
                    // state already left (stale timer) unless the instance is just busy
                    return instance.Status == WorkflowInstanceStatus.Busy ? JobOutcome.RetrySoon : JobOutcome.Done;

                case WorkflowJobKind.Timeout:
                    await RunClaimedAsync(instance.Id, WorkflowInstanceStatus.Active, actor, async run =>
                    {
                        await ExecuteChainAsync(run, TimeoutTransitionFor(run.Definition), null, "Timeout", cancellationToken);
                        if (run.Instance.WaitingForInstanceId is { } child)
                        {
                            run.CancelChildren.Add(child);
                            run.Instance.WaitingForInstanceId = null;
                        }
                    }, cancellationToken, (TimeoutTransition, null));
                    return JobOutcome.Done;

                case WorkflowJobKind.Transition:
                    await RunClaimedAsync(instance.Id, WorkflowInstanceStatus.Active, actor, run =>
                    {
                        var transition = ResolveTransition(run.Definition, run.Instance, job.TransitionKey!)
                            ?? throw new WorkflowConflictException($"Transition '{job.TransitionKey}' is not available in state '{run.Instance.State}'.");
                        return ExecuteChainAsync(run, transition, JobInput(job), nameof(TriggerType.Manual), cancellationToken);
                    }, cancellationToken, (job.TransitionKey!, JobInput(job)));
                    return JobOutcome.Done;

                case WorkflowJobKind.ResumeParent:
                    return await ResumeParentAsync(instance.Id, Guid.Parse(job.Payload!), cancellationToken) ? JobOutcome.Done : JobOutcome.RetrySoon;

                default:
                    return JobOutcome.Done;
            }
        }
        catch (WorkflowConflictException) when (instance.Status == WorkflowInstanceStatus.Busy || job.Kind != WorkflowJobKind.Transition)
        {
            return JobOutcome.RetrySoon; // busy right now – try again shortly
        }
    }

    private static JsonObject? JobInput(WorkflowJob job) => JsonPath.ParseObject(job.Payload) is { Count: > 0 } input ? input : null;

    // ───────────────────────────── queries ─────────────────────────────

    public Task<WorkflowInstanceModel> GetAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        GetAsync(null, instanceId, user, cancellationToken);

    internal async Task<WorkflowInstanceModel> GetAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        var instance = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        return await ToModelAsync(instance, user, cancellationToken);
    }

    public async Task<(List<WorkflowInstanceModel> Items, int Count)> ListAsync(
        string workflow,
        WorkflowUser user,
        string? state,
        WorkflowInstanceStatus? status,
        string? key,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var latest = registry.GetLatest(workflow);
        EnsureCanRead(latest, user);

        var query = db.Instances.AsNoTracking().Where(i => i.WorkflowKey == workflow);
        if (!CanSeeAllInstances(latest, user))
        {
            var owner = user.AuditName;
            query = query.Where(i => i.StartedBy == owner);
        }

        if (!string.IsNullOrEmpty(state))
        {
            query = query.Where(i => i.State == state);
        }

        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        if (!string.IsNullOrEmpty(key))
        {
            query = query.Where(i => i.BusinessKey == key);
        }

        var count = await query.CountAsync(cancellationToken);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var items = await query.OrderByDescending(i => i.CreatedAt)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var models = new List<WorkflowInstanceModel>(items.Count);
        foreach (var item in items)
        {
            models.Add(ToModel(item, user, []));
        }

        return (models, count);
    }

    public Task<List<WorkflowHistoryModel>> HistoryAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        HistoryAsync(null, instanceId, user, cancellationToken);

    internal async Task<List<WorkflowHistoryModel>> HistoryAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        return await db.History.AsNoTracking().Where(h => h.InstanceId == instanceId).OrderBy(h => h.Id)
            .Select(h => new WorkflowHistoryModel
            {
                TransitionKey = h.TransitionKey,
                FromState = h.FromState,
                ToState = h.ToState,
                Trigger = h.Trigger,
                ExecutedBy = h.ExecutedBy,
                StartedAt = h.StartedAt,
                FinishedAt = h.FinishedAt,
                DataVersion = h.DataVersion,
            })
            .ToListAsync(cancellationToken);
    }

    public Task<List<WorkflowIncidentModel>> IncidentsAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        IncidentsAsync(null, instanceId, user, cancellationToken);

    internal async Task<List<WorkflowIncidentModel>> IncidentsAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        var incidents = await db.Incidents.AsNoTracking().Where(i => i.InstanceId == instanceId).OrderBy(i => i.Id)
            .Select(i => new WorkflowIncidentModel
            {
                Id = i.Id,
                State = i.State,
                TransitionKey = i.TransitionKey,
                TaskKey = i.TaskKey,
                Error = i.Error,
                Attempts = i.Attempts,
                CreatedAt = i.CreatedAt,
                ResolvedAt = i.ResolvedAt,
            })
            .ToListAsync(cancellationToken);

        if (!options.Value.ExposeErrorDetails)
        {
            incidents.ForEach(i => i.Error = HiddenErrorText);
        }

        return incidents;
    }

    /// <summary>Shown instead of raw error texts unless <see cref="MiddtWorkflowOptions.ExposeErrorDetails"/> is on.</summary>
    public const string HiddenErrorText = "İşlem sırasında bir hata oluştu. Ayrıntılar sistem kayıtlarındadır.";

    // ───────────────────────────── pipeline ─────────────────────────────

    private sealed class Run(WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user)
    {
        public WorkflowDefinition Definition { get; } = definition;
        public WorkflowInstance Instance { get; } = instance;
        public JsonObject Data { get; set; } = data;
        public WorkflowUser User { get; } = user;

        /// <summary>State when the instance was claimed (scheduled jobs of an unchanged state are only known to the database).</summary>
        public string InitialState { get; } = instance.State;

        public JsonObject? Child { get; set; }
        public List<WorkflowJob> NewJobs { get; } = [];
        public HashSet<string> LeftStates { get; } = new(StringComparer.Ordinal);
        public bool ClearAllJobs { get; set; }
        public List<WorkflowEvent> Events { get; } = [];
        public List<Guid> CancelChildren { get; } = [];
        public bool ResolveIncidents { get; set; }
        public bool NotifyParent { get; set; }
    }

    internal enum JobOutcome
    {
        /// <summary>Executed or no longer relevant: delete the job.</summary>
        Done,

        /// <summary>Instance busy: retry with backoff.</summary>
        RetrySoon,

        /// <summary>Instance faulted: check again later without consuming attempts.</summary>
        Postpone,
    }

    private enum HopOutcome
    {
        Done,
        Faulted,
        Redirect,
    }

    private sealed class TaskFailure(string message, TaskDefinition task, ErrorPolicy policy, int attempts, Exception inner) : Exception(message, inner)
    {
        public TaskDefinition Task { get; } = task;
        public ErrorPolicy Policy { get; } = policy;
        public int Attempts { get; } = attempts;
    }

    /// <param name="pending">Transition (and input) recorded for <c>retry</c> if the command fails unexpectedly.</param>
    /// <param name="snapshot">
    /// Row the caller validated the command against. The claim then also requires its <c>Revision</c>, so the command never
    /// runs against a newer state than the one it was checked for, and the row does not have to be read again.
    /// </param>
    private async Task<Run> RunClaimedAsync(
        Guid instanceId,
        WorkflowInstanceStatus expected,
        WorkflowUser user,
        Func<Run, Task> body,
        CancellationToken cancellationToken,
        (string Key, JsonObject? Input)? pending = null,
        WorkflowInstance? snapshot = null)
    {
        // The Busy flag is the mutex: a conditional UPDATE lets exactly one caller in.
        var now = Now;
        var revision = snapshot?.Revision;
        var claimed = await db.Instances
            .Where(i => i.Id == instanceId && i.Status == expected && (revision == null || i.Revision == revision))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, WorkflowInstanceStatus.Busy)
                .SetProperty(i => i.BusySince, now)
                .SetProperty(i => i.Revision, i => i.Revision + 1), cancellationToken);

        if (claimed == 0)
        {
            var current = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
                ?? throw new WorkflowNotFoundException($"Instance '{instanceId}' does not exist.");
            throw new WorkflowConflictException(current.Status switch
            {
                WorkflowInstanceStatus.Busy => "The instance is busy executing another transition; try again.",
                _ when current.Status == expected => "The instance was changed concurrently; reload and try again.",
                _ => $"The instance is {current.Status}.",
            });
        }

        db.ChangeTracker.Clear();
        WorkflowInstance instance;
        if (snapshot is not null)
        {
            // Exactly the claimed row: apply the claim's changes and track it as unchanged.
            snapshot.Status = WorkflowInstanceStatus.Busy;
            snapshot.BusySince = now;
            snapshot.Revision++;
            db.Instances.Attach(snapshot);
            instance = snapshot;
        }
        else
        {
            instance = await db.Instances.FirstAsync(i => i.Id == instanceId, cancellationToken);
        }

        var run = new Run(registry.Get(instance.WorkflowKey, instance.WorkflowVersion), instance, JsonPath.ParseObject(instance.Data), user);

        try
        {
            await body(run);
            await SaveAsync(run, cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            // Never leave the instance Busy. Expected rejections restore the previous status; anything else faults the
            // instance with an incident and keeps the transition so it can be retried through the API.
            LogUnexpected(logger, ex, instanceId);
            db.ChangeTracker.Clear();
            var faulted = ex is not WorkflowException;
            var pendingKey = faulted ? pending?.Key : null;
            var pendingInput = faulted ? pending?.Input?.ToJsonString() : null;
            await db.Instances.Where(i => i.Id == instanceId && i.Status == WorkflowInstanceStatus.Busy)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, faulted ? WorkflowInstanceStatus.Faulted : expected)
                    .SetProperty(i => i.BusySince, (DateTime?)null)
                    .SetProperty(i => i.PendingTransition, i => faulted ? pendingKey : i.PendingTransition)
                    .SetProperty(i => i.PendingInput, i => faulted ? pendingInput : i.PendingInput)
                    .SetProperty(i => i.Error, i => faulted ? ex.Message : i.Error), CancellationToken.None);

            if (faulted)
            {
                db.Incidents.Add(new WorkflowIncident
                {
                    InstanceId = instanceId,
                    State = instance.State,
                    TransitionKey = pending?.Key,
                    Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message,
                    Attempts = 1,
                    CreatedAt = Now,
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private async Task ExecuteChainAsync(Run run, TransitionDefinition first, JsonObject? input, string trigger, CancellationToken cancellationToken)
    {
        var transition = first;
        for (var depth = 0; ; depth++)
        {
            if (depth > options.Value.MaxAutomaticChainLength)
            {
                Fault(run, transition, input, null, $"Automatic transition chain exceeded {options.Value.MaxAutomaticChainLength} steps (loop?).");
                return;
            }

            var (outcome, redirect) = await ExecuteHopAsync(run, transition, input, trigger, cancellationToken);
            if (outcome == HopOutcome.Faulted)
            {
                return;
            }

            if (outcome == HopOutcome.Redirect)
            {
                transition = redirect!;
                input = null;
                trigger = "Error";
                continue;
            }

            if (run.Instance.Status != WorkflowInstanceStatus.Busy || run.Instance.WaitingForInstanceId is not null || transition.Target == TransitionDefinition.Self)
            {
                return;
            }

            TransitionDefinition? next;
            try
            {
                next = FindAutomatic(run);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fault(run, transition, null, null, $"Evaluating automatic transitions failed: {ex.Message}");
                return;
            }

            if (next is null)
            {
                return;
            }

            transition = next;
            input = null;
            trigger = nameof(TriggerType.Automatic);
        }
    }

    private async Task<(HopOutcome, TransitionDefinition?)> ExecuteHopAsync(Run run, TransitionDefinition transition, JsonObject? input, string trigger, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        var definition = run.Definition;
        var from = instance.State;
        var startedAt = Now;

        // Rollback point: a failed hop leaves state and data exactly as they were before it.
        var dataBefore = (JsonObject)run.Data.DeepClone();
        var jobsBefore = run.NewJobs.ToList();
        var leftBefore = run.LeftStates.ToList();
        var waitingBefore = instance.WaitingForInstanceId;

        try
        {
            ApplyInput(run, transition, input);
            await RunTasksAsync(run, transition.OnExecute, transition.Key, input, cancellationToken);

            var target = transition.Target == TransitionDefinition.Self ? from : transition.Target;
            if (target != from)
            {
                if (definition.FindState(from) is { } leaving)
                {
                    await RunTasksAsync(run, leaving.OnExit, transition.Key, input, cancellationToken);
                    run.LeftStates.Add(from);
                    run.NewJobs.RemoveAll(j => j.Kind == WorkflowJobKind.Scheduled && j.State == from);
                }

                instance.State = target;
                var state = definition.FindState(target)!;
                await RunTasksAsync(run, state.OnEntry, transition.Key, input, cancellationToken);

                if (state.IsFinal)
                {
                    instance.Status = state.SubType == StateSubType.Cancelled ? WorkflowInstanceStatus.Cancelled : WorkflowInstanceStatus.Completed;
                    instance.CompletedAt = Now;
                    run.ClearAllJobs = true;
                    run.NewJobs.Clear();
                    run.NotifyParent = instance.ParentInstanceId is not null;
                }
                else
                {
                    foreach (var scheduled in state.Transitions.Where(t => t.Trigger == TriggerType.Scheduled))
                    {
                        run.NewJobs.Add(NewJob(instance, WorkflowJobKind.Scheduled, scheduled.Key, state.Key, Now + Durations.Parse(scheduled.After!)));
                    }

                    if (state.Type == StateType.SubFlow)
                    {
                        var child = await StartSubFlowAsync(run, state, cancellationToken);
                        if (child.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
                        {
                            // The child finished synchronously: continue right away instead of waiting for a resume job.
                            ApplyChildResult(run, state, child.Id, child.State, child.Status, child.StateSubType, child.Data);
                        }
                        else
                        {
                            instance.WaitingForInstanceId = child.Id;
                        }
                    }
                }
            }

            AddHistory(run, transition.Key, from, trigger, transition.Input?.Persistable(input) ?? input, startedAt);
            run.Events.Add(Event(run, instance.Status switch
            {
                WorkflowInstanceStatus.Completed => WorkflowEventType.Completed,
                WorkflowInstanceStatus.Cancelled => WorkflowEventType.Cancelled,
                _ => WorkflowEventType.StateChanged,
            }, from, transition.Key));

            return (HopOutcome.Done, null);
        }
        catch (TaskFailure failure)
        {
            run.Data = dataBefore;
            run.NewJobs.Clear();
            run.NewJobs.AddRange(jobsBefore);
            run.LeftStates.Clear();
            run.LeftStates.UnionWith(leftBefore);
            instance.State = from;
            instance.Status = WorkflowInstanceStatus.Busy;
            instance.CompletedAt = null;
            instance.WaitingForInstanceId = waitingBefore;
            run.ClearAllJobs = false;
            run.NotifyParent = false;

            AddIncident(run, transition.Key, failure.Task.DisplayKey, failure.InnerException?.Message ?? failure.Message, failure.Attempts);

            if (failure.Policy.Action == ErrorAction.Transition &&
                ResolveTransition(definition, instance, failure.Policy.Transition!) is { } errorTransition)
            {
                return (HopOutcome.Redirect, errorTransition);
            }

            Fault(run, transition, input, failure.Task.DisplayKey, failure.InnerException?.Message ?? failure.Message, incidentAdded: true);
            return (HopOutcome.Faulted, null);
        }
    }

    private async Task RunTasksAsync(Run run, List<TaskDefinition> taskList, string transitionKey, JsonObject? input, CancellationToken cancellationToken)
    {
        foreach (var task in taskList)
        {
            var policy = task.OnError ?? run.Definition.OnError ?? new ErrorPolicy();
            if (!string.IsNullOrWhiteSpace(task.When))
            {
                bool shouldRun;
                try
                {
                    shouldRun = expressions.EvaluateCondition(task.When, Scope(run, input));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A broken condition cannot be "continued": it faults unless the policy redirects to an error transition.
                    var conditionPolicy = new ErrorPolicy
                    {
                        Action = policy.Action == ErrorAction.Transition ? ErrorAction.Transition : ErrorAction.Fault,
                        Transition = policy.Transition,
                    };
                    throw new TaskFailure($"Condition of task '{task.DisplayKey}' failed.", task, conditionPolicy, 1, ex);
                }

                if (!shouldRun)
                {
                    continue;
                }
            }

            var implementation = tasks.Resolve(services, task.Task);
            var delay = Durations.Parse(policy.Delay);

            for (var attempt = 1; ; attempt++)
            {
                var context = new WorkflowTaskContext
                {
                    Task = task,
                    Data = run.Data,
                    Scope = Scope(run, input),
                    Expressions = expressions,
                    InstanceId = run.Instance.Id,
                    Workflow = run.Definition.Key,
                    State = run.Instance.State,
                    Services = services,
                    Logger = logger,
                    StartWorkflow = async (workflow, data, key, ct) => (await StartChildAsync(workflow, data, key, run.Instance.Id, run.User, ct)).Id,
                };

                try
                {
                    await implementation.ExecuteAsync(context, cancellationToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogTaskFailed(logger, ex, task.DisplayKey, run.Instance.Id, attempt, policy.MaxAttempts);
                    if (attempt < policy.MaxAttempts)
                    {
                        var wait = TimeSpan.FromTicks(Math.Min(delay.Ticks * (1L << Math.Min(attempt - 1, 20)), options.Value.MaxRetryDelay.Ticks));
                        await Task.Delay(wait, time, cancellationToken);
                        continue;
                    }

                    if (policy.Action == ErrorAction.Continue)
                    {
                        AddIncident(run, transitionKey, task.DisplayKey, ex.Message, attempt);
                        break;
                    }

                    throw new TaskFailure($"Task '{task.DisplayKey}' failed after {attempt} attempt(s).", task, policy, attempt, ex);
                }
            }
        }
    }

    private void ApplyInput(Run run, TransitionDefinition transition, JsonObject? input)
    {
        if (transition.Input is not { } definition)
        {
            return; // no declared input: the body is never written into instance data
        }

        // The body is only written when a schema validated it; an input block with just "set" ignores the body.
        if (input is not null && definition.Schema is not null && definition.Persistable(input) is { } persistable)
        {
            JsonPath.Merge(run.Data, persistable, definition.Into);
        }

        foreach (var (path, expression) in definition.Set)
        {
            JsonPath.Set(run.Data, path, expressions.EvaluateToNode(expression, Scope(run, input)));
        }
    }

    private static void ValidateInput(TransitionDefinition transition, JsonObject? input)
    {
        if (transition.Input?.Schema is { } schema)
        {
            var errors = JsonSchemaValidator.Validate(input ?? [], schema);
            if (errors.Count > 0)
            {
                throw new WorkflowValidationException(errors);
            }
        }
    }

    private TransitionDefinition? FindAutomatic(Run run)
    {
        var state = run.Definition.FindState(run.Instance.State);
        if (state is null || state.IsFinal)
        {
            return null;
        }

        var automatic = state.Transitions.Where(t => t.Trigger == TriggerType.Automatic).ToList();
        var scope = Scope(run, null);
        return automatic.Where(t => !t.Default).FirstOrDefault(t => expressions.EvaluateCondition(t.Rule!, scope))
            ?? automatic.FirstOrDefault(t => t.Default);
    }

    private async Task<WorkflowInstanceModel> StartSubFlowAsync(Run run, StateDefinition state, CancellationToken cancellationToken)
    {
        var subFlow = state.SubFlow!;
        JsonObject childData;
        if (subFlow.Input.Count == 0)
        {
            childData = (JsonObject)run.Data.DeepClone();
        }
        else
        {
            childData = [];
            var scope = Scope(run, null);
            foreach (var (path, expression) in subFlow.Input)
            {
                JsonPath.Set(childData, path, expressions.EvaluateToNode(expression, scope));
            }
        }

        return await StartChildAsync(subFlow.Workflow, childData, null, run.Instance.Id, run.User, cancellationToken);
    }

    private static void ApplyChildResult(Run run, StateDefinition state, Guid childId, string childState, WorkflowInstanceStatus childStatus, string childSubType, JsonObject childData)
    {
        if (state.SubFlow?.OutputInto is { Length: > 0 } into)
        {
            JsonPath.Set(run.Data, into, childData);
        }
        else
        {
            JsonPath.Merge(run.Data, childData);
        }

        run.Child = new JsonObject
        {
            ["id"] = childId,
            ["state"] = childState,
            ["status"] = childStatus.ToString(),
            ["subType"] = childSubType,
            ["data"] = childData.DeepClone(),
        };
        run.Instance.WaitingForInstanceId = null;
    }

    /// <summary>Starts a workflow in its own scope/transaction (the child is committed independently of the parent).</summary>
    private async Task<WorkflowInstanceModel> StartChildAsync(string workflow, JsonObject data, string? key, Guid parentId, WorkflowUser user, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();
        // Children run as the system on behalf of the parent's actor (role checks were done on the parent).
        var actor = user.IsSystem ? user : new WorkflowUser { Name = user.AuditName, IsSystem = true };
        var (child, _) = await engine.StartAsync(workflow, new StartWorkflowRequest { Key = key, Data = data }, actor, parentId, cancellationToken);
        return child;
    }

    /// <summary>Applies a finished child's data to its waiting parent and continues the parent's automatic transitions.</summary>
    private async Task<bool> ResumeParentAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        var child = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == childId, cancellationToken);
        var parent = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == parentId, cancellationToken);
        if (child is null || parent is null || parent.IsTerminal)
        {
            return true;
        }

        // A child that finishes synchronously inside its parent's pipeline arrives before the parent committed
        // WaitingForInstanceId: retry once the parent is no longer busy.
        if (parent.Status == WorkflowInstanceStatus.Busy)
        {
            return false;
        }

        if (parent.Status != WorkflowInstanceStatus.Active || parent.WaitingForInstanceId != childId)
        {
            return true; // already resumed, faulted or moved on
        }

        var childDefinition = registry.Get(child.WorkflowKey, child.WorkflowVersion);
        var childState = childDefinition.FindState(child.State);
        await RunClaimedAsync(parentId, WorkflowInstanceStatus.Active, WorkflowUser.System, async run =>
        {
            var state = run.Definition.FindState(run.Instance.State)!;
            ApplyChildResult(run, state, child.Id, child.State, child.Status, (childState?.SubType ?? StateSubType.None).ToString(), JsonPath.ParseObject(child.Data));
            AddHistory(run, ResumeTransition, run.Instance.State, "SubFlow", null, Now);

            if (FindAutomatic(run) is { } next)
            {
                await ExecuteChainAsync(run, next, null, nameof(TriggerType.Automatic), cancellationToken);
            }
        }, cancellationToken);

        return true;
    }

    // ───────────────────────────── persistence ─────────────────────────────

    private async Task SaveAsync(Run run, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        var data = run.Data.ToJsonString();
        if (data != instance.Data)
        {
            instance.Data = data;
            instance.DataVersion++;
        }

        if (instance.Status == WorkflowInstanceStatus.Busy)
        {
            instance.Status = WorkflowInstanceStatus.Active;
        }

        instance.BusySince = null;
        instance.UpdatedAt = Now;
        instance.Revision++;

        foreach (var history in db.ChangeTracker.Entries<WorkflowHistory>().Where(e => e.State == EntityState.Added))
        {
            history.Entity.DataVersion = instance.DataVersion;
        }

        // SaveChanges is atomic on its own; an explicit transaction is only needed around the bulk statements.
        var bulk = run.ClearAllJobs || run.LeftStates.Count > 0 || run.ResolveIncidents;
        await using var transaction = bulk && db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        if (run.ClearAllJobs)
        {
            await db.Jobs.Where(j => j.InstanceId == instance.Id).ExecuteDeleteAsync(cancellationToken);
        }
        else if (run.LeftStates.Count > 0)
        {
            var leftStates = run.LeftStates.ToList();
            await db.Jobs.Where(j => j.InstanceId == instance.Id && j.Kind == WorkflowJobKind.Scheduled && leftStates.Contains(j.State!))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (run.ResolveIncidents)
        {
            var now = Now;
            await db.Incidents.Where(i => i.InstanceId == instance.Id && i.ResolvedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ResolvedAt, now), cancellationToken);
        }

        db.Jobs.AddRange(run.NewJobs);
        await db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterCommitAsync(run, cancellationToken);
    }

    private async Task AfterCommitAsync(Run run, CancellationToken cancellationToken)
    {
        if (run.NewJobs.Any(j => j.DueAt <= Now.AddSeconds(1)))
        {
            jobSignal.Notify();
        }

        foreach (var child in run.CancelChildren)
        {
            await CancelChildAsync(child, cancellationToken);
        }

        if (run.NotifyParent && run.Instance.ParentInstanceId is { } parentId)
        {
            bool resumed;
            try
            {
                resumed = await ResumeParentAsync(parentId, run.Instance.Id, cancellationToken);
            }
            catch (WorkflowConflictException)
            {
                resumed = false;
            }

            if (!resumed)
            {
                db.Jobs.Add(NewJob(new WorkflowInstance { Id = parentId }, WorkflowJobKind.ResumeParent, null, null, Now.AddSeconds(1), run.Instance.Id.ToString()));
                await db.SaveChangesAsync(cancellationToken);
                jobSignal.Notify();
            }
        }

        foreach (var workflowEvent in run.Events)
        {
            foreach (var handler in EventHandlers)
            {
                try
                {
                    await handler.HandleAsync(workflowEvent, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogHandlerFailed(logger, ex, handler.GetType().Name, workflowEvent.Type, workflowEvent.InstanceId);
                }
            }
        }
    }

    private async Task CancelChildAsync(Guid childId, CancellationToken cancellationToken)
    {
        var now = Now;
        await db.Instances
            .Where(i => i.Id == childId && i.Status != WorkflowInstanceStatus.Completed && i.Status != WorkflowInstanceStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, WorkflowInstanceStatus.Cancelled)
                .SetProperty(i => i.CompletedAt, now)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);
        await db.Jobs.Where(j => j.InstanceId == childId).ExecuteDeleteAsync(cancellationToken);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private void Fault(Run run, TransitionDefinition transition, JsonObject? input, string? taskKey, string error, bool incidentAdded = false)
    {
        run.Instance.Status = WorkflowInstanceStatus.Faulted;
        run.Instance.PendingTransition = transition.Key;
        run.Instance.PendingInput = (transition.Input?.Persistable(input) ?? input)?.ToJsonString();
        run.Instance.Error = error;
        if (!incidentAdded)
        {
            AddIncident(run, transition.Key, taskKey, error, 1);
        }

        run.Events.Add(Event(run, WorkflowEventType.Faulted, run.Instance.State, transition.Key));
    }

    private void AddIncident(Run run, string transitionKey, string? taskKey, string error, int attempts) =>
        db.Incidents.Add(new WorkflowIncident
        {
            InstanceId = run.Instance.Id,
            State = run.Instance.State,
            TransitionKey = transitionKey,
            TaskKey = taskKey,
            Error = error.Length > 2000 ? error[..2000] : error,
            Attempts = attempts,
            CreatedAt = Now,
        });

    private void AddHistory(Run run, string transitionKey, string from, string trigger, JsonObject? input, DateTime startedAt) =>
        db.History.Add(new WorkflowHistory
        {
            InstanceId = run.Instance.Id,
            TransitionKey = transitionKey,
            FromState = string.IsNullOrEmpty(from) ? null : from,
            ToState = run.Instance.State,
            Trigger = trigger,
            ExecutedBy = run.User.AuditName,
            Input = input?.ToJsonString(),
            Data = run.Data.ToJsonString(),
            StartedAt = startedAt,
            FinishedAt = Now,
        });

    private WorkflowJob NewJob(WorkflowInstance instance, WorkflowJobKind kind, string? transition, string? state, DateTime dueAt, string? payload = null, string? requestedBy = null) => new()
    {
        Id = db.NewId(),
        InstanceId = instance.Id,
        Kind = kind,
        TransitionKey = transition,
        State = state,
        DueAt = dueAt,
        Payload = payload,
        RequestedBy = requestedBy,
        CreatedAt = Now,
    };

    private IWorkflowEventHandler[]? eventHandlers;

    private IWorkflowEventHandler[] EventHandlers => eventHandlers ??= [.. services.GetServices<IWorkflowEventHandler>()];

    /// <summary>The data snapshot is only cloned when someone will receive the event.</summary>
    private WorkflowEvent Event(Run run, WorkflowEventType type, string? from, string? transition) =>
        new(type, run.Instance.Id, run.Definition.Key, run.Definition.Version, run.Instance.BusinessKey, from, run.Instance.State, transition,
            EventHandlers.Length > 0 ? (JsonObject)run.Data.DeepClone() : [], Now);

    private ExpressionScope Scope(Run run, JsonObject? input) => new()
    {
        Data = run.Data,
        Input = input,
        Child = run.Child,
        User = run.User,
        Now = time.GetUtcNow(),
        Instance = new JsonObject
        {
            ["id"] = run.Instance.Id,
            ["key"] = run.Instance.BusinessKey,
            ["state"] = run.Instance.State,
            ["workflow"] = run.Definition.Key,
            ["version"] = run.Definition.Version,
        },
    };

    private static TransitionDefinition TimeoutTransitionFor(WorkflowDefinition definition) => new()
    {
        Key = TimeoutTransition,
        Target = definition.Timeout!.Target,
        Trigger = TriggerType.Automatic,
    };

    /// <summary>Any transition (state, shared, start, timeout) by key, as available in the instance's current state.</summary>
    private static TransitionDefinition? ResolveTransition(WorkflowDefinition definition, WorkflowInstance instance, string key)
    {
        if (key == definition.Start.Key && string.IsNullOrEmpty(instance.State))
        {
            return definition.Start;
        }

        if (key == TimeoutTransition && definition.Timeout is not null)
        {
            return TimeoutTransitionFor(definition);
        }

        return AvailableIn(definition, instance.State).FirstOrDefault(t => t.Key == key);
    }

    private static IEnumerable<TransitionDefinition> AvailableIn(WorkflowDefinition definition, string stateKey)
    {
        var state = definition.FindState(stateKey);
        if (state is null || state.IsFinal)
        {
            return [];
        }

        return state.Transitions.Concat(definition.SharedTransitions.Where(t => t.AvailableIn.Contains("*") || t.AvailableIn.Contains(stateKey)));
    }

    private TransitionDefinition FindManualTransition(WorkflowDefinition definition, WorkflowInstance instance, string key, WorkflowUser user)
    {
        if (instance.IsTerminal)
        {
            throw new WorkflowConflictException($"The instance is {instance.Status}.");
        }

        var transition = AvailableIn(definition, instance.State).FirstOrDefault(t => t.Key == key && t.Trigger == TriggerType.Manual)
            ?? throw new WorkflowConflictException($"Transition '{key}' is not available in state '{instance.State}'.");

        if (!CanExecute(transition, user))
        {
            throw new WorkflowForbiddenException($"You are not allowed to execute '{key}'.");
        }

        if (!IsRuleSatisfied(transition, definition, instance, JsonPath.ParseObject(instance.Data), user))
        {
            throw new WorkflowConflictException($"Transition '{key}' is not available for the current data.");
        }

        if (instance.WaitingForInstanceId is not null && !IsCancellation(definition, transition))
        {
            throw new WorkflowConflictException("The instance is waiting for a subflow to finish.");
        }

        return transition;
    }

    /// <summary>Availability condition of a manual transition (<c>rule</c>); evaluation errors make it unavailable.</summary>
    private bool IsRuleSatisfied(TransitionDefinition transition, WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user)
    {
        if (transition.Trigger != TriggerType.Manual || string.IsNullOrWhiteSpace(transition.Rule))
        {
            return true;
        }

        try
        {
            return expressions.EvaluateCondition(transition.Rule, new ExpressionScope
            {
                Data = data,
                User = user,
                Now = time.GetUtcNow(),
                Instance = new JsonObject
                {
                    ["id"] = instance.Id,
                    ["key"] = instance.BusinessKey,
                    ["state"] = instance.State,
                    ["workflow"] = definition.Key,
                    ["version"] = definition.Version,
                },
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRuleFailed(logger, ex, transition.Key, instance.Id);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Availability rule of transition {Transition} failed for instance {InstanceId}")]
    private static partial void LogRuleFailed(ILogger logger, Exception exception, string transition, Guid instanceId);

    private static bool IsCancellation(WorkflowDefinition definition, TransitionDefinition transition) =>
        definition.FindState(transition.Target) is { IsFinal: true };

    private static bool CanExecute(TransitionDefinition transition, WorkflowUser user) =>
        transition.Roles.Count == 0 || transition.Roles.Any(user.IsInRole);

    /// <summary>Definition roles plus, in <c>owner</c> mode, ownership of the instance (others get 404: no existence disclosure).</summary>
    private static void EnsureCanAccess(WorkflowDefinition definition, WorkflowInstance instance, WorkflowUser user)
    {
        EnsureCanRead(definition, user);
        if (!CanSeeAllInstances(definition, user) && !string.Equals(instance.StartedBy, user.AuditName, StringComparison.Ordinal))
        {
            throw new WorkflowNotFoundException($"Instance '{instance.Id}' does not exist.");
        }
    }

    private static bool CanSeeAllInstances(WorkflowDefinition definition, WorkflowUser user) =>
        user.IsSystem
        || definition.InstanceAccess is not { IsOwnerOnly: true } access
        || access.SupervisorRoles.Any(user.IsInRole);

    private static void EnsureCanRead(WorkflowDefinition definition, WorkflowUser user)
    {
        if (definition.Roles.Count > 0 && !definition.Roles.Any(user.IsInRole))
        {
            throw new WorkflowForbiddenException($"You are not allowed to access workflow '{definition.Key}'.");
        }
    }

    private static void EnsureActive(WorkflowInstance instance)
    {
        if (instance.Status != WorkflowInstanceStatus.Active)
        {
            throw new WorkflowConflictException($"The instance is {instance.Status}.");
        }
    }

    /// <summary>Loads the instance and applies the read checks (roles, owner mode, expected workflow → 403/404).</summary>
    private async Task<WorkflowInstance> FindAccessibleAsync(Guid id, string? workflow, WorkflowUser user, CancellationToken cancellationToken)
    {
        var instance = await FindAsync(id, cancellationToken);
        EnsureCanAccess(registry.Get(instance.WorkflowKey, instance.WorkflowVersion), instance, user);
        if (workflow is not null && !string.Equals(instance.WorkflowKey, workflow, StringComparison.Ordinal))
        {
            throw new WorkflowNotFoundException($"Instance '{id}' does not belong to workflow '{workflow}'.");
        }

        return instance;
    }

    /// <summary>
    /// Response of a command, built from the committed in-memory run. Scheduled jobs are only read from the database when
    /// the instance stayed in the state it was claimed in (their jobs were not created by this run).
    /// </summary>
    private async Task<WorkflowInstanceModel> ToModelAsync(Run run, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        if (!run.ClearAllJobs && instance.State == run.InitialState && !run.LeftStates.Contains(instance.State))
        {
            return await ToModelAsync(instance, run.User, cancellationToken);
        }

        var scheduled = run.ClearAllJobs
            ? []
            : run.NewJobs
                .Where(j => j.Kind == WorkflowJobKind.Scheduled && j.State == instance.State)
                .OrderBy(j => j.DueAt)
                .Select(j => new ScheduledTransitionModel { Key = j.TransitionKey!, DueAt = j.DueAt })
                .ToList();
        return ToModel(instance, run.User, scheduled);
    }

    private async Task<WorkflowInstance> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
        ?? throw new WorkflowNotFoundException($"Instance '{id}' does not exist.");

    private async Task<WorkflowInstanceModel> ToModelAsync(WorkflowInstance instance, WorkflowUser user, CancellationToken cancellationToken)
    {
        var scheduled = await db.Jobs.AsNoTracking()
            .Where(j => j.InstanceId == instance.Id && j.Kind == WorkflowJobKind.Scheduled)
            .OrderBy(j => j.DueAt)
            .Select(j => new ScheduledTransitionModel { Key = j.TransitionKey!, DueAt = j.DueAt })
            .ToListAsync(cancellationToken);

        return ToModel(instance, user, scheduled);
    }

    private WorkflowInstanceModel ToModel(WorkflowInstance instance, WorkflowUser user, List<ScheduledTransitionModel> scheduled)
    {
        var definition = registry.Get(instance.WorkflowKey, instance.WorkflowVersion);
        var state = definition.FindState(instance.State);

        var data = JsonPath.ParseObject(instance.Data);
        var available = instance.Status == WorkflowInstanceStatus.Active
            ? AvailableIn(definition, instance.State)
                .Where(t => t.Trigger == TriggerType.Manual && CanExecute(t, user))
                .Where(t => instance.WaitingForInstanceId is null || IsCancellation(definition, t))
                .Where(t => IsRuleSatisfied(t, definition, instance, data, user))
                .Select(t => new AvailableTransitionModel
                {
                    Key = t.Key,
                    Title = t.Title,
                    Target = t.Target,
                    InputSchema = (JsonObject?)t.Input?.Schema?.DeepClone(),
                    View = string.IsNullOrWhiteSpace(t.View?.Component) ? null : t.View.Component,
                    Inline = t.View?.Inline == true,
                    InputInto = t.Input?.Into,
                })
                .ToList()
            : [];

        return new WorkflowInstanceModel
        {
            Id = instance.Id,
            Key = instance.BusinessKey,
            Workflow = instance.WorkflowKey,
            WorkflowVersion = instance.WorkflowVersion,
            State = instance.State,
            StateTitle = state?.Title,
            StateType = state?.Type.ToString() ?? string.Empty,
            StateSubType = state?.SubType.ToString() ?? string.Empty,
            View = string.IsNullOrWhiteSpace(state?.View?.Component) ? null : state.View.Component,
            Layout = string.IsNullOrWhiteSpace(state?.View?.Layout) ? null : state.View.Layout,
            Status = instance.Status,
            Data = data,
            DataVersion = instance.DataVersion,
            ParentInstanceId = instance.ParentInstanceId,
            WaitingForInstanceId = instance.WaitingForInstanceId,
            Error = instance.Error is null ? null : options.Value.ExposeErrorDetails ? instance.Error : HiddenErrorText,
            StartedBy = instance.StartedBy,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
            AvailableTransitions = available,
            ScheduledTransitions = scheduled,
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Workflow instance {InstanceId} failed unexpectedly")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow task {Task} of instance {InstanceId} failed (attempt {Attempt}/{MaxAttempts})")]
    private static partial void LogTaskFailed(ILogger logger, Exception exception, string task, Guid instanceId, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Workflow event handler {Handler} failed for {EventType} of instance {InstanceId}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handler, WorkflowEventType eventType, Guid instanceId);
}
