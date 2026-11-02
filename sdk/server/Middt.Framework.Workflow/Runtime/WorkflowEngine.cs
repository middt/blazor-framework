using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Diagnostics;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Endpoints;
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
    WorkflowJobNotifier jobNotifier,
    ILogger<WorkflowEngine> logger)
{
    internal const string TimeoutTransition = "$timeout";
    internal const string ResumeTransition = "$subflow-completed";
    internal const string BackgroundTransition = "$background";

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
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.start {workflow}");
        activity?.SetTag("middt.workflow.key", workflow);
        if (!string.IsNullOrEmpty(request.Key))
        {
            activity?.SetTag("middt.workflow.business_key", request.Key);
        }

        request.Data = WorkflowHttpInput.EnsureEnvelope(request.Data);
        var definition = registry.GetLatest(workflow);
        EnsureCanRead(definition, user);

        if (!string.IsNullOrEmpty(request.Key) &&
            await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.WorkflowKey == workflow && i.BusinessKey == request.Key, cancellationToken) is { } existing)
        {
            // Same read checks as GET: in owner mode another user's instance is not disclosed through its business key.
            EnsureCanAccess(registry.Get(existing.WorkflowKey, existing.WorkflowVersion), existing, user);
            return (await ToModelAsync(existing, user, cancellationToken), false);
        }

        ValidateInput(definition.Start, request.Data);

        var startBody = WorkflowHttpInput.ExtractBody(request.Data, definition.Start.Input?.Schema);
        var startPending = (definition.Start.Input?.Persistable(startBody)
            ?? (parentInstanceId is not null ? startBody : null))?.ToJsonString();

        var now = Now;
        var instanceId = db.NewId();
        var instance = new WorkflowInstance
        {
            Id = instanceId,
            WorkflowKey = definition.Key,
            WorkflowVersion = definition.Version,
            BusinessKey = string.IsNullOrEmpty(request.Key) ? null : request.Key,
            Status = WorkflowInstanceStatus.Busy,
            BusySince = now,
            PendingTransition = definition.Start.Key,
            PendingInput = startPending,
            ParentInstanceId = parentInstanceId,
            StartedBy = user.AuditName,
            LastExecutedBy = user.AuditName,
            CreatedAt = now,
            UpdatedAt = now,
            InstanceData = new WorkflowInstanceData
            {
                InstanceId = instanceId,
                WorkflowKey = definition.Key,
                Data = "{}",
                DataVersion = 0,
                DataHash = WorkflowInstanceData.EmptyJsonObjectHash,
                ETag = WorkflowInstanceData.NewETag(instanceId, 0),
                UpdatedAt = now,
            },
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
            EnsureCanAccess(registry.Get(winner.WorkflowKey, winner.WorkflowVersion), winner, user);
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
            await using (StartBusyHeartbeat(instance.Id))
            {
                await ExecuteChainAsync(run, definition.Start, request.Data, "Start", cancellationToken);
            }

            await SaveAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            // Same guarantee as RunClaimedAsync: never leave the new instance Busy; it can be retried from the start transition.
            LogUnexpected(logger, ex, instance.Id);
            db.ChangeTracker.Clear();
            await db.Instances.Where(i => i.Id == instance.Id && i.Status == WorkflowInstanceStatus.Busy)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, WorkflowInstanceStatus.Faulted)
                    .SetProperty(i => i.BusySince, (DateTime?)null)
                    .SetProperty(i => i.PendingTransition, definition.Start.Key)
                    .SetProperty(i => i.PendingInput, startPending)
                    .SetProperty(i => i.Error, ex.Message), CancellationToken.None);
            db.Incidents.Add(new WorkflowIncident
            {
                InstanceId = instance.Id,
                WorkflowKey = definition.Key,
                State = instance.State,
                TransitionKey = definition.Start.Key,
                Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message,
                Attempts = 1,
                CreatedAt = Now,
            });
            await db.SaveChangesAsync(CancellationToken.None);
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
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.execute_transition {workflow ?? "instance"}/{transitionKey}");
        if (!string.IsNullOrEmpty(workflow))
        {
            activity?.SetTag("middt.workflow.key", workflow);
        }

        activity?.SetTag("middt.workflow.instance_id", instanceId.ToString());
        activity?.SetTag("middt.workflow.transition", transitionKey);

        input = WorkflowHttpInput.EnsureEnvelope(input);
        var snapshot = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        var definition = registry.Get(snapshot.WorkflowKey, snapshot.WorkflowVersion);

        if (snapshot.WaitingForInstanceId is { } waitingChildId &&
            snapshot.Status == WorkflowInstanceStatus.Active &&
            definition.FindState(snapshot.State)?.SubFlow?.ForwardTransitions == true &&
            !AvailableIn(definition, snapshot.State).Any(t => t.Key == transitionKey && t.Trigger == TriggerType.Manual))
        {
            await ExecuteTransitionAsync(null, waitingChildId, transitionKey, input, user, runAsync, cancellationToken);
            return await GetAsync(workflow, instanceId, user, cancellationToken);
        }

        var transition = FindManualTransition(definition, snapshot, transitionKey, user, input);

        if (runAsync)
        {
            EnsureActive(snapshot);
            if (transition.Input?.Sensitive.Count > 0)
            {
                throw new WorkflowValidationException(["Transitions with sensitive input cannot run asynchronously."]);
            }

            if (transition.Input?.Format == InputFormat.JsonPatch)
            {
                throw new WorkflowValidationException(["Transitions with a jsonPatch input cannot run asynchronously (a failed patch must reach the caller)."]);
            }

            db.Jobs.Add(NewJob(snapshot, WorkflowJobKind.Transition, transitionKey, snapshot.State, Now, input?.ToJsonString(), user.AuditName));
            await db.SaveChangesAsync(cancellationToken);
            await jobNotifier.NotifyAsync(db, cancellationToken);
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
        }, cancellationToken, (transition.Key, transition.Input?.Persistable(WorkflowHttpInput.ExtractBody(input, transition.Input?.Schema))), snapshot);

        return await ToModelAsync(run, cancellationToken);
    }

    /// <summary>Re-runs the failed transition of a Faulted instance with its original input.</summary>
    public Task<WorkflowInstanceModel> RetryAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        RetryAsync(null, instanceId, user, cancellationToken);

    internal async Task<WorkflowInstanceModel> RetryAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.retry {workflow ?? "instance"}");
        if (!string.IsNullOrEmpty(workflow))
        {
            activity?.SetTag("middt.workflow.key", workflow);
        }

        activity?.SetTag("middt.workflow.instance_id", instanceId.ToString());

        var snapshot = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);

        if (snapshot.Status != WorkflowInstanceStatus.Faulted || snapshot.PendingTransition is null)
        {
            throw new WorkflowConflictException("Only faulted instances can be retried.");
        }

        var run = await RunClaimedAsync(instanceId, WorkflowInstanceStatus.Faulted, user, async run =>
        {
            if (run.Instance.PendingTransition == BackgroundTransition)
            {
                await RetryBackgroundAsync(run, cancellationToken);
                return;
            }

            var transition = ResolveTransition(run.Definition, run.Instance, run.Instance.PendingTransition!)
                ?? throw new WorkflowConflictException($"Transition '{run.Instance.PendingTransition}' is no longer available.");
            var input = JsonPath.ParseObject(run.Instance.PendingInput);
            run.Instance.PendingTransition = null;
            run.Instance.PendingInput = null;
            run.Instance.Error = null;
            run.ResolveIncidents = true;
            if (transition.Key == run.Definition.Start.Key && run.Instance.ParentInstanceId is not null
                && transition.Input is null && input.Count > 0)
            {
                run.Data = (JsonObject)input.DeepClone();
            }
            await ExecuteChainAsync(run, transition, input.Count == 0 ? null : input, "Retry", cancellationToken);
        }, cancellationToken, (snapshot.PendingTransition, JsonPath.ParseObject(snapshot.PendingInput) is { Count: > 0 } retryInput ? retryInput : null), snapshot);

        return await ToModelAsync(run, cancellationToken);
    }

    /// <summary>Executes a due job (called by <see cref="WorkflowJobProcessor"/>).</summary>
    internal async Task<JobOutcome> ProcessJobAsync(WorkflowJob job, CancellationToken cancellationToken)
    {
        if (job.Kind == WorkflowJobKind.Event)
        {
            return await DeliverEventAsync(job, cancellationToken) ? JobOutcome.Done : JobOutcome.RetrySoon;
        }

        var instance = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == job.InstanceId, cancellationToken);
        if (instance is null || instance.IsTerminal)
        {
            return JobOutcome.Done;
        }

        // Buffered messages expire on their own clock, whatever the instance status.
        if (job.Kind == WorkflowJobKind.Message)
        {
            return await ProcessMessageJobAsync(job, instance, cancellationToken);
        }

        // Timers of a faulted instance wait until it is retried (checked again later, without consuming attempts).
        if (instance.Status == WorkflowInstanceStatus.Faulted && job.Kind is not (WorkflowJobKind.ResumeParent or WorkflowJobKind.CancelChild))
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
                    return await ResumeParentAsync(instance.Id, Guid.Parse(job.Payload!.AsSpan().Trim(" \"".AsSpan())), cancellationToken) ? JobOutcome.Done : JobOutcome.RetrySoon;

                case WorkflowJobKind.CancelChild:
                    return await CancelChildAsync(instance.Id, cancellationToken) ? JobOutcome.Done : JobOutcome.RetrySoon;

                case WorkflowJobKind.Task:
                    return await ProcessBackgroundJobAsync(job, instance, cancellationToken);

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
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.get {workflow ?? "instance"}");
        if (!string.IsNullOrEmpty(workflow))
        {
            activity?.SetTag("middt.workflow.key", workflow);
        }

        activity?.SetTag("middt.workflow.instance_id", instanceId.ToString());

        var instance = await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        return await ToModelAsync(instance, user, cancellationToken);
    }

    public Task<(List<WorkflowInstanceModel> Items, int Count)> ListAsync(
        string workflow,
        WorkflowUser user,
        string? state,
        WorkflowInstanceStatus? status,
        string? key,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ListAsync(workflow, user, state, status, key, page, pageSize, null, cancellationToken);

    public async Task<(List<WorkflowInstanceModel> Items, int Count)> ListAsync(
        string workflow,
        WorkflowUser user,
        string? state,
        WorkflowInstanceStatus? status,
        string? key,
        int page,
        int pageSize,
        IReadOnlyDictionary<string, string?>? filter,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.list {workflow}");
        activity?.SetTag("middt.workflow.key", workflow);
        if (!string.IsNullOrEmpty(state))
        {
            activity?.SetTag("middt.workflow.state", state);
        }

        var versions = registry.GetVersions(workflow);
        var readable = versions
            .Where(d => d.Roles.Count == 0 || d.Roles.Any(user.IsInRole))
            .ToArray();
        if (readable.Length == 0)
        {
            throw new WorkflowForbiddenException($"You are not allowed to access workflow '{workflow}'.");
        }

        var allVersions = readable.Where(d => CanSeeAllInstances(d, user)).Select(d => d.Version).ToArray();
        var ownerVersions = readable.Where(d => !CanSeeAllInstances(d, user)).Select(d => d.Version).ToArray();
        var owner = user.AuditName;

        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking();
        if (ownerVersions.Length == 0 && allVersions.Length == versions.Count)
        {
            query = query.Where(i => i.WorkflowKey == workflow);
        }
        else if (allVersions.Length == 0 && ownerVersions.Length == versions.Count)
        {
            query = query.Where(i => i.WorkflowKey == workflow && i.StartedBy == owner);
        }
        else
        {
            query = query.Where(i => i.WorkflowKey == workflow
                && (allVersions.Contains(i.WorkflowVersion) || (ownerVersions.Contains(i.WorkflowVersion) && i.StartedBy == owner)));
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

        var projectedSchema = JsonSchemaValidator.ProjectSchema(readable[^1].DataSchema, user);
        var (sqlConditions, remainingFilters, createdFromUtc, createdToUtc) = WorkflowPartitionManager.ClassifyDataFilters(filter, projectedSchema);
        if (sqlConditions.Count > 0 || createdFromUtc is not null || createdToUtc is not null)
        {
            query = WorkflowPartitionManager.ApplySqlDataFilters(query, db, options.Value.Schema, workflow, sqlConditions, createdFromUtc, createdToUtc);
        }

        pageSize = Math.Clamp(pageSize, 1, 200);
        var currentPage = Math.Max(page, 1);

        if (remainingFilters.Count == 0)
        {
            var count = await query.IgnoreAutoIncludes().CountAsync(cancellationToken);
            var items = await query.OrderByDescending(i => i.CreatedAt)
                .Skip((currentPage - 1) * pageSize).Take(pageSize)
                .ToListAsync(cancellationToken);

            var models = new List<WorkflowInstanceModel>(items.Count);
            foreach (var item in items)
            {
                models.Add(ToModel(item, user, []));
            }

            return (models, count);
        }

        var candidates = await query.OrderByDescending(i => i.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var matched = new List<WorkflowInstanceModel>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var model = ToModel(candidate, user, []);
            if (WorkflowPartitionManager.MatchesDataFilters(model.Data, remainingFilters))
            {
                matched.Add(model);
            }
        }

        var paged = matched.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
        return (paged, matched.Count);
    }

    public Task<List<WorkflowHistoryModel>> HistoryAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        HistoryAsync(null, instanceId, user, cancellationToken);

    internal async Task<List<WorkflowHistoryModel>> HistoryAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.history {workflow ?? "instance"}");
        if (!string.IsNullOrEmpty(workflow))
        {
            activity?.SetTag("middt.workflow.key", workflow);
        }

        activity?.SetTag("middt.workflow.instance_id", instanceId.ToString());

        await FindAccessibleAsync(instanceId, workflow, user, cancellationToken);
        var rows = await db.History.AsNoTracking().Where(h => h.InstanceId == instanceId).OrderBy(h => h.Id)
            .Select(h => new
            {
                h.TransitionKey,
                h.FromState,
                h.ToState,
                h.Trigger,
                h.ExecutedBy,
                h.StartedAt,
                h.FinishedAt,
                h.DataVersion,
                h.Tasks,
            })
            .ToListAsync(cancellationToken);

        activity?.SetTag("middt.workflow.result_count", rows.Count);
        return rows.Select(h => new WorkflowHistoryModel
        {
            TransitionKey = h.TransitionKey,
            FromState = h.FromState,
            ToState = h.ToState,
            Trigger = h.Trigger,
            ExecutedBy = h.ExecutedBy,
            StartedAt = h.StartedAt,
            FinishedAt = h.FinishedAt,
            DataVersion = h.DataVersion,
            Tasks = string.IsNullOrEmpty(h.Tasks)
                ? []
                : JsonSerializer.Deserialize<List<WorkflowTaskExecutionModel>>(h.Tasks, WorkflowJson.Options) ?? [],
        }).ToList();
    }

    /// <summary>
    /// The instance data after each history row, oldest first – rebuilt from snapshots (<c>history: full</c>) and patches
    /// (<c>history: delta</c>), so both modes answer the same question. Empty for <c>history: none</c>.
    /// </summary>
    public async Task<List<(string TransitionKey, int DataVersion, JsonObject Data)>> HistoryDataAsync(
        Guid instanceId,
        WorkflowUser user,
        CancellationToken cancellationToken = default)
    {
        await FindAccessibleAsync(instanceId, null, user, cancellationToken);
        var rows = await db.History.AsNoTracking().Where(h => h.InstanceId == instanceId).OrderBy(h => h.Id)
            .Select(h => new { h.TransitionKey, h.DataVersion, h.Data, h.Patch })
            .ToListAsync(cancellationToken);

        var current = new JsonObject();
        var result = new List<(string, int, JsonObject)>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Data is not null)
            {
                current = JsonPath.ParseObject(row.Data);
            }
            else if (row.Patch is not null && JsonNode.Parse(row.Patch) is JsonArray patch)
            {
                JsonPatch.Apply(current, patch);
            }

            result.Add((row.TransitionKey, row.DataVersion, (JsonObject)current.DeepClone()));
        }

        return result;
    }

    public Task<List<WorkflowIncidentModel>> IncidentsAsync(Guid instanceId, WorkflowUser user, CancellationToken cancellationToken = default) =>
        IncidentsAsync(null, instanceId, user, cancellationToken);

    internal async Task<List<WorkflowIncidentModel>> IncidentsAsync(string? workflow, Guid instanceId, WorkflowUser user, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.incidents {workflow ?? "instance"}");
        if (!string.IsNullOrEmpty(workflow))
        {
            activity?.SetTag("middt.workflow.key", workflow);
        }

        activity?.SetTag("middt.workflow.instance_id", instanceId.ToString());

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

        activity?.SetTag("middt.workflow.result_count", incidents.Count);
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
        public WorkflowInstanceModel? ActiveSubFlowModel { get; set; }
        public List<WorkflowTaskExecutionModel> CurrentHopTasks { get; } = [];
        public List<WorkflowJob> NewJobs { get; } = [];
        public HashSet<string> LeftStates { get; } = new(StringComparer.Ordinal);
        public bool ClearAllJobs { get; set; }
        public List<WorkflowEvent> Events { get; } = [];
        public List<Guid> CancelChildren { get; } = [];
        public bool ResolveIncidents { get; set; }
        public bool NotifyParent { get; set; }

        /// <summary>Tasks that failed with <c>"action": "Continue"</c> in this run (a background run reports them back).</summary>
        public List<(string TaskKey, string Error, int Attempts)> ContinuedErrors { get; } = [];

        /// <summary>Stable prefix for task idempotency keys (a background job retried after a crash keeps its keys).</summary>
        public string? IdempotencyScope { get; set; }

        /// <summary>Buffered messages were woken because the instance now waits for them.</summary>
        public bool MessagesWoken { get; set; }
    }

    internal enum JobOutcome
    {
        /// <summary>Executed or no longer relevant: delete the job.</summary>
        Done,

        /// <summary>Instance busy: retry with backoff.</summary>
        RetrySoon,

        /// <summary>Instance faulted: check again later without consuming attempts.</summary>
        Postpone,

        /// <summary>The engine already moved the job (new due time, lease released): leave it as it is.</summary>
        Rescheduled,
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
        var previousPendingKey = snapshot?.PendingTransition;
        var previousPendingInput = snapshot?.PendingInput;
        var pendingKeyForClaim = pending?.Key;
        var pendingInputForClaim = pending?.Input?.ToJsonString();
        var claimed = await db.Instances
            .Where(i => i.Id == instanceId && i.Status == expected && (revision == null || i.Revision == revision))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, WorkflowInstanceStatus.Busy)
                .SetProperty(i => i.BusySince, now)
                .SetProperty(i => i.PendingTransition, i => pendingKeyForClaim ?? i.PendingTransition)
                .SetProperty(i => i.PendingInput, i => pendingKeyForClaim == null ? i.PendingInput : pendingInputForClaim)
                .SetProperty(i => i.Revision, i => i.Revision + 1), cancellationToken);

        if (claimed == 0)
        {
            var current = await db.Instances.IgnoreAutoIncludes().AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
                ?? throw new WorkflowNotFoundException($"Instance '{instanceId}' does not exist.");
            throw new WorkflowClaimException(current.Status switch
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
            if (pendingKeyForClaim is not null)
            {
                snapshot.PendingTransition = pendingKeyForClaim;
                snapshot.PendingInput = pendingInputForClaim;
            }

            var attachNewData = snapshot.InstanceData is null;
            snapshot.InstanceData ??= new WorkflowInstanceData
            {
                InstanceId = snapshot.Id,
                WorkflowKey = snapshot.WorkflowKey,
                Data = "{}",
                DataVersion = 0,
                DataHash = WorkflowInstanceData.EmptyJsonObjectHash,
                ETag = WorkflowInstanceData.NewETag(snapshot.Id, 0),
                UpdatedAt = now,
            };
            db.Instances.Attach(snapshot);
            if (attachNewData)
            {
                db.Entry(snapshot.InstanceData).State = EntityState.Added;
            }

            instance = snapshot;
        }
        else
        {
            instance = await db.Instances.FirstAsync(i => i.Id == instanceId, cancellationToken);
            if (instance.InstanceData is null)
            {
                instance.InstanceData = new WorkflowInstanceData
                {
                    InstanceId = instance.Id,
                    WorkflowKey = instance.WorkflowKey,
                    Data = "{}",
                    DataVersion = 0,
                    DataHash = WorkflowInstanceData.EmptyJsonObjectHash,
                    ETag = WorkflowInstanceData.NewETag(instance.Id, 0),
                    UpdatedAt = now,
                };
                db.InstanceData.Add(instance.InstanceData);
            }
        }

        var run = new Run(registry.Get(instance.WorkflowKey, instance.WorkflowVersion), instance, JsonPath.ParseObject(instance.Data), user);

        try
        {
            await using (StartBusyHeartbeat(instanceId))
            {
                await body(run);
            }

            await SaveAsync(run, cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            // Never leave the instance Busy. Expected rejections restore the previous status; anything else faults the
            // instance with an incident and keeps the transition so it can be retried through the API.
            LogUnexpected(logger, ex, instanceId);
            db.ChangeTracker.Clear();
            var faulted = ex is not WorkflowException && pending is not null;
            var pendingKey = faulted ? pending?.Key : null;
            var pendingInput = faulted ? pending?.Input?.ToJsonString() : null;
            await db.Instances.Where(i => i.Id == instanceId && i.Status == WorkflowInstanceStatus.Busy)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, faulted ? WorkflowInstanceStatus.Faulted : expected)
                    .SetProperty(i => i.BusySince, (DateTime?)null)
                    .SetProperty(i => i.PendingTransition, faulted ? pendingKey : previousPendingKey)
                    .SetProperty(i => i.PendingInput, faulted ? pendingInput : previousPendingInput)
                    .SetProperty(i => i.Error, i => faulted ? ex.Message : i.Error), CancellationToken.None);

            if (faulted)
            {
                db.Incidents.Add(new WorkflowIncident
                {
                    InstanceId = instanceId,
                    WorkflowKey = run.Definition.Key,
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

            if (run.Instance.Status != WorkflowInstanceStatus.Busy ||
                run.Instance.WaitingForInstanceId is not null ||
                run.Instance.BackgroundTaskId is not null ||
                (transition.Target == TransitionDefinition.Self && trigger == nameof(TriggerType.Automatic)))
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
        var hopStart = time.GetTimestamp();
        run.CurrentHopTasks.Clear();
        using var activity = WorkflowDiagnostics.StartTransitionActivity(definition.Key, definition.Version, instance.Id, from, transition.Key, trigger);

        // Rollback point: a failed hop leaves state and data exactly as they were before it.
        var dataBefore = (JsonObject)run.Data.DeepClone();
        var jobsBefore = run.NewJobs.ToList();
        var leftBefore = run.LeftStates.ToList();
        var waitingBefore = instance.WaitingForInstanceId;
        var backgroundBefore = instance.BackgroundTaskId;
        var activeSubFlowBefore = run.ActiveSubFlowModel;
        var keyBefore = instance.BusinessKey;
        var lastExecutedByBefore = instance.LastExecutedBy;

        try
        {
            var currentStatePolicy = definition.FindState(from)?.OnError;
            ApplyInput(run, transition, input);
            await RunTasksAsync(run, transition.OnExecute, transition.Key, "onExecute", currentStatePolicy, input, cancellationToken);

            var target = transition.Target == TransitionDefinition.Self ? from : transition.Target;
            var isReenter = transition.Target == TransitionDefinition.Self && transition.Reenter && !string.IsNullOrEmpty(from);
            if (target != from || isReenter)
            {
                if (definition.FindState(from) is { } leaving)
                {
                    await RunTasksAsync(run, leaving.OnExit, transition.Key, "onExit", leaving.OnError, input, cancellationToken);
                    run.LeftStates.Add(from);
                    run.NewJobs.RemoveAll(j => j.Kind == WorkflowJobKind.Scheduled && j.State == from);
                }

                instance.State = target;
                instance.BackgroundTaskId = null; // a result of the left state's background task is discarded
                var state = definition.FindState(target)!;
                await RunTasksAsync(run, state.OnEntry, transition.Key, "onEntry", state.OnError, input, cancellationToken);

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
                        var dueAt = ResolveScheduledDueAt(run, scheduled, input);
                        run.NewJobs.Add(NewJob(instance, WorkflowJobKind.Scheduled, scheduled.Key, state.Key, dueAt));
                    }

                    if (state.Type == StateType.SubFlow)
                    {
                        var child = await StartSubFlowAsync(run, state, input, cancellationToken);
                        if (child.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
                        {
                            // The child finished synchronously: continue right away instead of waiting for a resume job.
                            ApplyChildResult(run, state, child.Id, child.State, child.Status, child.StateSubType, child.Data);
                        }
                        else
                        {
                            instance.WaitingForInstanceId = child.Id;
                            run.ActiveSubFlowModel = child;
                        }
                    }
                }
            }

            if (trigger is nameof(TriggerType.Manual) or "Start")
            {
                instance.LastExecutedBy = run.User.AuditName;
            }

            AddHistory(run, transition.Key, from, trigger, transition.Input?.Persistable(WorkflowHttpInput.ExtractBody(input, transition.Input?.Schema)) ?? input, startedAt);
            run.Events.Add(Event(run, instance.Status switch
            {
                WorkflowInstanceStatus.Completed => WorkflowEventType.Completed,
                WorkflowInstanceStatus.Cancelled => WorkflowEventType.Cancelled,
                _ => WorkflowEventType.StateChanged,
            }, from, transition.Key));

            WorkflowDiagnostics.CompleteTransitionActivity(
                activity,
                definition.Key,
                transition.Key,
                trigger,
                instance.State,
                instance.Status.ToString(),
                nameof(HopOutcome.Done),
                time.GetElapsedTime(hopStart).TotalMilliseconds);

            return (HopOutcome.Done, null);
        }
        catch (TaskFailure failure)
        {
            var stateAtFailure = instance.State;
            run.Data = dataBefore;
            run.NewJobs.Clear();
            run.NewJobs.AddRange(jobsBefore);
            run.LeftStates.Clear();
            run.LeftStates.UnionWith(leftBefore);
            instance.State = from;
            instance.Status = WorkflowInstanceStatus.Busy;
            instance.CompletedAt = null;
            instance.WaitingForInstanceId = waitingBefore;
            instance.BackgroundTaskId = backgroundBefore;
            run.ActiveSubFlowModel = activeSubFlowBefore;
            instance.BusinessKey = keyBefore;
            instance.LastExecutedBy = lastExecutedByBefore;
            run.CurrentHopTasks.Clear();
            run.ClearAllJobs = false;
            run.NotifyParent = false;

            var errorMessage = failure.InnerException?.Message ?? failure.Message;
            AddIncident(run, transition.Key, failure.Task.DisplayKey, errorMessage, failure.Attempts);

            if (failure.Policy.Action == ErrorAction.Transition)
            {
                var errorTransition = ResolveTransition(definition, instance, failure.Policy.Transition!)
                    ?? (!string.IsNullOrEmpty(stateAtFailure) ? AvailableIn(definition, stateAtFailure).FirstOrDefault(t => t.Key == failure.Policy.Transition) : null);
                if (errorTransition is not null)
                {
                    WorkflowDiagnostics.CompleteTransitionActivity(
                        activity,
                        definition.Key,
                        transition.Key,
                        trigger,
                        from,
                        instance.Status.ToString(),
                        nameof(HopOutcome.Redirect),
                        time.GetElapsedTime(hopStart).TotalMilliseconds,
                        errorMessage);
                    return (HopOutcome.Redirect, errorTransition);
                }
            }

            Fault(run, transition, input, failure.Task.DisplayKey, errorMessage, incidentAdded: true);
            WorkflowDiagnostics.CompleteTransitionActivity(
                activity,
                definition.Key,
                transition.Key,
                trigger,
                instance.State,
                instance.Status.ToString(),
                nameof(HopOutcome.Faulted),
                time.GetElapsedTime(hopStart).TotalMilliseconds,
                errorMessage);
            return (HopOutcome.Faulted, null);
        }
    }

    private DateTime ResolveScheduledDueAt(Run run, TransitionDefinition scheduled, JsonObject? input)
    {
        if (!string.IsNullOrWhiteSpace(scheduled.After))
        {
            var afterText = scheduled.After.Contains("{{", StringComparison.Ordinal)
                ? expressions.RenderText(scheduled.After, Scope(run, input)).Trim()
                : scheduled.After;
            return Durations.TryParse(afterText, out var duration)
                ? Now + duration
                : throw new InvalidOperationException($"Scheduled transition '{scheduled.Key}' produced an invalid 'after' duration '{afterText}'.");
        }

        if (!string.IsNullOrWhiteSpace(scheduled.At))
        {
            var atText = scheduled.At.Contains("{{", StringComparison.Ordinal)
                ? expressions.RenderText(scheduled.At, Scope(run, input)).Trim()
                : scheduled.At;
            return DateTimeOffset.TryParse(atText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dto)
                ? dto.UtcDateTime
                : throw new InvalidOperationException($"Scheduled transition '{scheduled.Key}' produced an invalid 'at' timestamp '{atText}'.");
        }

        throw new InvalidOperationException($"Scheduled transition '{scheduled.Key}' has neither 'after' nor 'at'.");
    }

    private async Task RunTasksAsync(
        Run run,
        List<TaskDefinition> taskList,
        string transitionKey,
        string hook,
        ErrorPolicy? stateErrorPolicy,
        JsonObject? input,
        CancellationToken cancellationToken,
        int indexOffset = 0)
    {
        for (var listIndex = 0; listIndex < taskList.Count; listIndex++)
        {
            var task = taskList[listIndex];
            var taskIndex = indexOffset + listIndex;
            var policy = task.OnError ?? stateErrorPolicy ?? run.Definition.OnError ?? new ErrorPolicy();
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

            if (task.Background && hook == "onEntry")
            {
                // Commit the state now; the task (and the onEntry tasks after it) continue in a job, outside the claim.
                var job = NewJob(run.Instance, WorkflowJobKind.Task, transitionKey, run.Instance.State, Now,
                    new JsonObject { ["taskIndex"] = taskIndex }.ToJsonString(), run.User.AuditName);
                run.NewJobs.Add(job);
                run.Instance.BackgroundTaskId = job.Id;
                return;
            }

            var implementation = tasks.Resolve(services, task.Task);
            var delay = Durations.Parse(policy.Delay);
            var maxDelayCap = policy.MaxDelay is { } md && Durations.TryParse(md, out var parsedMax)
                ? Math.Min(parsedMax.Ticks, options.Value.MaxRetryDelay.Ticks)
                : options.Value.MaxRetryDelay.Ticks;
            var multiplier = Math.Max(policy.BackoffMultiplier, 1.0);
            var taskStart = time.GetTimestamp();

            for (var attempt = 1; ; attempt++)
            {
                using var taskActivity = WorkflowDiagnostics.StartTaskActivity(
                    run.Definition.Key,
                    run.Instance.Id,
                    transitionKey,
                    hook,
                    task.DisplayKey,
                    task.Task,
                    attempt);
                using var childStartLock = new SemaphoreSlim(1, 1);
                var attemptData = (JsonObject)run.Data.DeepClone();
                var originalBusinessKey = run.Instance.BusinessKey;
                var context = new WorkflowTaskContext
                {
                    Task = task,
                    Data = attemptData,
                    Scope = Scope(run, input, attemptData),
                    Expressions = expressions,
                    InstanceId = run.Instance.Id,
                    IdempotencyKey = $"{run.IdempotencyScope ?? $"{run.Instance.Id:N}:{run.Instance.Revision}"}:{transitionKey}:{taskIndex}",
                    Workflow = run.Definition.Key,
                    State = run.Instance.State,
                    Services = services,
                    Logger = logger,
                    StartWorkflow = async (workflow, data, key, ct) =>
                    {
                        await childStartLock.WaitAsync(ct);
                        try
                        {
                            return (await StartChildAsync(workflow, data, key, run.Instance.Id, run.User, ct)).Id;
                        }
                        finally
                        {
                            childStartLock.Release();
                        }
                    },
                    BusinessKey = run.Instance.BusinessKey,
                    SetBusinessKey = (key, ct) => SetBusinessKeyAsync(run, key, ct),
                };

                try
                {
                    await implementation.ExecuteAsync(context, cancellationToken);
                    run.Data = attemptData;
                    var elapsedMs = time.GetElapsedTime(taskStart).TotalMilliseconds;
                    run.CurrentHopTasks.Add(new WorkflowTaskExecutionModel
                    {
                        TaskKey = task.DisplayKey,
                        TaskType = task.Task,
                        Hook = hook,
                        Attempts = attempt,
                        DurationMs = (long)elapsedMs,
                        Outcome = "Completed",
                    });
                    WorkflowDiagnostics.CompleteTaskActivity(taskActivity, run.Definition.Key, task.Task, hook, "Completed", elapsedMs);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    run.Instance.BusinessKey = originalBusinessKey;
                    LogTaskFailed(logger, ex, task.DisplayKey, run.Instance.Id, attempt, policy.MaxAttempts);
                    if (attempt < policy.MaxAttempts && ShouldRetry(policy, ex))
                    {
                        var factor = Math.Pow(multiplier, Math.Min(attempt - 1, 20));
                        var waitTicks = (long)Math.Min(delay.Ticks * factor, maxDelayCap);
                        if (policy.UseJitter && waitTicks > 0)
                        {
                            var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
                            waitTicks = (long)Math.Min(waitTicks * jitter, maxDelayCap);
                        }

                        await Task.Delay(TimeSpan.FromTicks(Math.Max(0, waitTicks)), time, cancellationToken);
                        continue;
                    }

                    var elapsedMs = time.GetElapsedTime(taskStart).TotalMilliseconds;
                    if (policy.Action == ErrorAction.Continue)
                    {
                        AddIncident(run, transitionKey, task.DisplayKey, ex.Message, attempt);
                        run.ContinuedErrors.Add((task.DisplayKey, ex.Message, attempt));
                        run.CurrentHopTasks.Add(new WorkflowTaskExecutionModel
                        {
                            TaskKey = task.DisplayKey,
                            TaskType = task.Task,
                            Hook = hook,
                            Attempts = attempt,
                            DurationMs = (long)elapsedMs,
                            Outcome = "ContinuedOnError",
                        });
                        WorkflowDiagnostics.CompleteTaskActivity(taskActivity, run.Definition.Key, task.Task, hook, "ContinuedOnError", elapsedMs, ex.Message);
                        break;
                    }

                    WorkflowDiagnostics.CompleteTaskActivity(taskActivity, run.Definition.Key, task.Task, hook, "Failed", elapsedMs, ex.Message);
                    throw new TaskFailure($"Task '{task.DisplayKey}' failed after {attempt} attempt(s).", task, policy, attempt, ex);
                }
            }
        }
    }

    private static bool ShouldRetry(ErrorPolicy policy, Exception ex)
    {
        if (policy.RetryOnStatusCodes.Count == 0)
        {
            return true;
        }

        int? statusCode = ex switch
        {
            WorkflowTaskException { StatusCode: { } sc } => sc,
            HttpRequestException { StatusCode: { } hsc } => (int)hsc,
            _ => null,
        };

        if (statusCode is null)
        {
            return true;
        }

        var code = statusCode.Value;
        var hasPositive = false;
        var matchedPositive = false;

        foreach (var rawRule in policy.RetryOnStatusCodes)
        {
            var rule = rawRule.Trim();
            if (string.IsNullOrEmpty(rule))
            {
                continue;
            }

            if (rule.StartsWith('!'))
            {
                if (MatchesStatusCode(rule[1..], code))
                {
                    return false;
                }
            }
            else
            {
                hasPositive = true;
                if (MatchesStatusCode(rule, code))
                {
                    matchedPositive = true;
                }
            }
        }

        return !hasPositive || matchedPositive;
    }

    private static bool MatchesStatusCode(string pattern, int statusCode)
    {
        var codeText = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (pattern.Length == 3 && pattern.EndsWith("xx", StringComparison.OrdinalIgnoreCase) && codeText.Length == 3)
        {
            return codeText[0] == pattern[0];
        }

        return string.Equals(pattern, codeText, StringComparison.Ordinal);
    }

    private void ApplyInput(Run run, TransitionDefinition transition, JsonObject? input)
    {
        if (transition.Input is not { } definition)
        {
            return; // no declared input: the body is never written into instance data
        }

        if (definition.Format == InputFormat.JsonPatch)
        {
            ApplyPatchInput(run, definition, input);
        }
        else if (definition.Schema is not null
            && WorkflowHttpInput.ExtractBody(input, definition.Schema) is { } target
            && definition.Persistable(target) is { } persistable)
        {
            // The body is only written when a schema validated it; an input block with just "set" ignores the body.
            JsonPath.Merge(run.Data, persistable, definition.Into);
        }

        foreach (var (path, expression) in definition.Set)
        {
            JsonPath.Set(run.Data, path, expressions.EvaluateToNode(expression, Scope(run, input)));
        }
    }

    /// <summary>
    /// <c>input.format: jsonPatch</c>: applies the client's patch to the data at <c>into</c> (atomically), validates the result
    /// with the schema and writes it back. A failed <c>test</c> operation is a conflict (409), anything else a 400.
    /// </summary>
    private static void ApplyPatchInput(Run run, InputDefinition definition, JsonObject? input)
    {
        var patch = (JsonArray)WorkflowHttpInput.ExtractBody(input, definition.Schema)![PatchProperty]!;
        var target = JsonPath.Get(run.Data, definition.Into) is JsonObject current ? (JsonObject)current.DeepClone() : [];
        try
        {
            JsonPatch.ApplyStrict(target, patch);
        }
        catch (JsonPatchException ex) when (ex.IsTestFailure)
        {
            throw new WorkflowConflictException($"JSON Patch {ex.Message}");
        }
        catch (JsonPatchException ex)
        {
            throw new WorkflowValidationException([$"JSON Patch {ex.Message}"]);
        }

        var errors = JsonSchemaValidator.Validate(target, definition.Schema!);
        if (errors.Count > 0)
        {
            throw new WorkflowValidationException(errors);
        }

        JsonPath.Set(run.Data, definition.Into!, target);
    }

    /// <summary>Body property that carries a JSON Patch (<c>{ "patch": [...] }</c>; a bare array body is wrapped into it).</summary>
    internal const string PatchProperty = "patch";

    private static void ValidateInput(TransitionDefinition transition, JsonObject? input)
    {
        if (transition.Input is { Format: InputFormat.JsonPatch } patchInput)
        {
            var body = WorkflowHttpInput.ExtractBody(input, patchInput.Schema);
            var errors = JsonPatch.Validate(body?[PatchProperty]);
            if (errors.Count > 0)
            {
                throw new WorkflowValidationException(errors);
            }

            return;
        }

        if (transition.Input?.Schema is { } schema)
        {
            var target = WorkflowHttpInput.ExtractBody(input, schema);
            var errors = JsonSchemaValidator.Validate(target ?? [], schema);
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

    private async Task<WorkflowInstanceModel> StartSubFlowAsync(Run run, StateDefinition state, JsonObject? input, CancellationToken cancellationToken)
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
            var scope = Scope(run, input);
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

    /// <summary>
    /// <c>setKey</c>: gives the instance its business key once (same key again = no-op). The unique index on
    /// (workflow, business key) stays the last line of defence against a concurrent claim; this check makes the usual
    /// conflict a clear task error instead of a failed commit.
    /// </summary>
    private async Task SetBusinessKeyAsync(Run run, string key, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        if (string.Equals(instance.BusinessKey, key, StringComparison.Ordinal))
        {
            return;
        }

        if (instance.BusinessKey is not null)
        {
            throw new WorkflowTaskException($"setKey: the instance already has the business key '{instance.BusinessKey}' (cannot change it to '{key}').");
        }

        var id = instance.Id;
        var workflow = instance.WorkflowKey;
        if (await db.Instances.AsNoTracking().AnyAsync(i => i.WorkflowKey == workflow && i.BusinessKey == key && i.Id != id, cancellationToken))
        {
            throw new WorkflowTaskException($"setKey: the business key '{key}' is already used by another instance of '{workflow}'.");
        }

        instance.BusinessKey = key;
    }

    /// <summary>Starts a workflow in its own scope/transaction (the child is committed independently of the parent).</summary>
    private async Task<WorkflowInstanceModel> StartChildAsync(string workflow, JsonObject data, string? key, Guid parentId, WorkflowUser user, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.subflow.start {workflow}");
        activity?.SetTag("middt.workflow.key", workflow);
        activity?.SetTag("middt.workflow.parent_instance_id", parentId.ToString());

        await using var scope = scopeFactory.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();
        // Children run as the system on behalf of the parent's actor (role checks were done on the parent).
        var actor = user.IsSystem ? user : new WorkflowUser { Name = user.AuditName, IsSystem = true };
        var (child, _) = await engine.StartAsync(workflow, new StartWorkflowRequest { Key = key, Data = data }, actor, parentId, cancellationToken);
        activity?.SetTag("middt.workflow.child_instance_id", child.Id.ToString());
        return child;
    }

    /// <summary>Applies a finished child's data to its waiting parent and continues the parent's automatic transitions.</summary>
    private async Task<bool> ResumeParentAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity("workflow.subflow.resume_parent");
        activity?.SetTag("middt.workflow.parent_instance_id", parentId.ToString());
        activity?.SetTag("middt.workflow.child_instance_id", childId.ToString());

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

        if (instance.Status != WorkflowInstanceStatus.Faulted)
        {
            instance.PendingTransition = null;
            instance.PendingInput = null;
        }

        instance.BusySince = null;
        instance.UpdatedAt = Now;
        instance.Revision++;

        using (var persistActivity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.persist {run.Definition.Key}"))
        {
            persistActivity?.SetTag("middt.workflow.key", run.Definition.Key);
            persistActivity?.SetTag("middt.workflow.instance_id", instance.Id.ToString());
            persistActivity?.SetTag("middt.workflow.state", instance.State);
            persistActivity?.SetTag("middt.workflow.status", instance.Status.ToString());
            persistActivity?.SetTag("middt.workflow.data_version", instance.DataVersion);
            persistActivity?.SetTag("middt.workflow.revision", instance.Revision);

            // SaveChanges is atomic on its own; an explicit transaction is only needed around the bulk statements.
            // A state that takes messages wakes the ones buffered for it.
            var wakeMessages = instance.Status == WorkflowInstanceStatus.Active && instance.WaitingForInstanceId is null
                ? EventsAvailableIn(run.Definition, instance.State)
                : [];
            var bulk = run.ClearAllJobs || run.LeftStates.Count > 0 || run.ResolveIncidents || wakeMessages.Count > 0;
            await using var transaction = bulk && db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

            if (run.ClearAllJobs)
            {
                await db.Jobs.Where(j => j.InstanceId == instance.Id
                    && j.Kind != WorkflowJobKind.Event && j.Kind != WorkflowJobKind.CancelChild).ExecuteDeleteAsync(cancellationToken);
            }
            else if (run.LeftStates.Count > 0)
            {
                var leftStates = run.LeftStates.ToList();
                await db.Jobs.Where(j => j.InstanceId == instance.Id
                        && (j.Kind == WorkflowJobKind.Scheduled || j.Kind == WorkflowJobKind.Task) && leftStates.Contains(j.State!))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (wakeMessages.Count > 0)
            {
                var now = Now;
                run.MessagesWoken = await db.Jobs.Where(j => j.InstanceId == instance.Id && j.Kind == WorkflowJobKind.Message
                        && j.DueAt > now && wakeMessages.Contains(j.TransitionKey!))
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.DueAt, now), cancellationToken) > 0;
            }

            if (run.ResolveIncidents)
            {
                var now = Now;
                await db.Incidents.Where(i => i.InstanceId == instance.Id && i.ResolvedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ResolvedAt, now), cancellationToken);
            }

            foreach (var child in run.CancelChildren.Distinct())
            {
                run.NewJobs.Add(NewJob(new WorkflowInstance { Id = child }, WorkflowJobKind.CancelChild, null, null, Now));
            }

            if (run.NotifyParent && instance.ParentInstanceId is { } parentId)
            {
                run.NewJobs.Add(NewJob(new WorkflowInstance { Id = parentId }, WorkflowJobKind.ResumeParent, null, null, Now, JsonSerializer.Serialize(instance.Id)));
            }

            if (EventHandlers.Length > 0)
            {
                foreach (var workflowEvent in run.Events)
                {
                    run.NewJobs.Add(NewJob(instance, WorkflowJobKind.Event, null, null, Now.AddSeconds(5), JsonSerializer.Serialize(workflowEvent)));
                }
            }

            db.Jobs.AddRange(run.NewJobs);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        try
        {
            await AfterCommitAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            // The transition is committed. Durable jobs will finish follow-up work after a process restart.
            LogUnexpected(logger, ex, instance.Id);
        }
    }

    private async Task AfterCommitAsync(Run run, CancellationToken cancellationToken)
    {
        if (run.NewJobs.Count > 0 || run.MessagesWoken)
        {
            await jobNotifier.NotifyAsync(db, cancellationToken);
        }

        foreach (var child in run.CancelChildren)
        {
            await CancelChildAsync(child, cancellationToken);
        }

        if (run.NotifyParent && run.Instance.ParentInstanceId is { } parentId)
        {
            try
            {
                await ResumeParentAsync(parentId, run.Instance.Id, cancellationToken);
            }
            catch (WorkflowConflictException)
            {
                // The persisted resume job retries after the parent claim ends.
            }
        }

        foreach (var eventJob in run.NewJobs.Where(j => j.Kind == WorkflowJobKind.Event))
        {
            if (await DeliverEventAsync(eventJob, cancellationToken))
            {
                await db.Jobs.Where(j => j.Id == eventJob.Id).ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    private async Task<bool> DeliverEventAsync(WorkflowJob job, CancellationToken cancellationToken)
    {
        var workflowEvent = JsonSerializer.Deserialize<WorkflowEvent>(job.Payload!)!;
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.event.deliver {workflowEvent.Type}");
        activity?.SetTag("middt.workflow.key", workflowEvent.Workflow);
        activity?.SetTag("middt.workflow.instance_id", workflowEvent.InstanceId.ToString());
        activity?.SetTag("middt.workflow.event_type", workflowEvent.Type.ToString());

        var delivered = true;
        foreach (var handler in EventHandlers)
        {
            var handlerName = handler.GetType().Name;
            using var handlerActivity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.event.handle {handlerName}");
            handlerActivity?.SetTag("middt.workflow.event_handler", handlerName);
            try
            {
                await handler.HandleAsync(workflowEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                handlerActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                LogHandlerFailed(logger, ex, handlerName, workflowEvent.Type, workflowEvent.InstanceId);
                delivered = false;
            }
        }

        return delivered;
    }

    private async Task<bool> CancelChildAsync(Guid childId, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity("workflow.subflow.cancel_child");
        activity?.SetTag("middt.workflow.child_instance_id", childId.ToString());

        var now = Now;
        var changed = await db.Instances
            .Where(i => i.Id == childId && i.Status != WorkflowInstanceStatus.Busy
                && i.Status != WorkflowInstanceStatus.Completed && i.Status != WorkflowInstanceStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, WorkflowInstanceStatus.Cancelled)
                .SetProperty(i => i.CompletedAt, now)
                .SetProperty(i => i.UpdatedAt, now)
                .SetProperty(i => i.Revision, i => i.Revision + 1), cancellationToken);
        if (changed == 0)
        {
            var status = await db.Instances.AsNoTracking().Where(i => i.Id == childId)
                .Select(i => (WorkflowInstanceStatus?)i.Status).FirstOrDefaultAsync(cancellationToken);
            if (status == WorkflowInstanceStatus.Busy)
            {
                return false;
            }
        }

        await db.Jobs.Where(j => j.InstanceId == childId
            && j.Kind != WorkflowJobKind.CancelChild && j.Kind != WorkflowJobKind.Event).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private void Fault(Run run, TransitionDefinition transition, JsonObject? input, string? taskKey, string error, bool incidentAdded = false)
    {
        run.Instance.Status = WorkflowInstanceStatus.Faulted;
        run.Instance.PendingTransition = transition.Key;
        run.Instance.PendingInput = (transition.Input?.Persistable(WorkflowHttpInput.ExtractBody(input, transition.Input?.Schema)) ?? input)?.ToJsonString();
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
            WorkflowKey = run.Definition.Key,
            State = run.Instance.State,
            TransitionKey = transitionKey,
            TaskKey = taskKey,
            Error = error.Length > 2000 ? error[..2000] : error,
            Attempts = attempts,
            CreatedAt = Now,
        });

    private void AddHistory(Run run, string transitionKey, string from, string trigger, JsonObject? input, DateTime startedAt)
    {
        var data = run.Data.ToJsonString();
        var previous = run.Instance.Data;
        if (data != previous)
        {
            run.Instance.Data = data;
            run.Instance.DataVersion++;
        }

        var mode = run.Definition.History;
        if (mode == HistoryMode.None)
        {
            run.CurrentHopTasks.Clear();
            return;
        }

        string? snapshot = data;
        string? patch = null;
        if (mode == HistoryMode.Delta)
        {
            var candidate = data == previous ? "[]" : JsonPatch.Diff(JsonPath.ParseObject(previous), run.Data).ToJsonString();
            var since = run.Instance.HistoryPatchCount;
            var checkpoint = since is null                                              // first row (or rows written before delta)
                || since.Value + 1 >= run.Definition.HistorySnapshotEvery               // bound the replay length
                || run.Instance.IsTerminal                                              // the final data in one read
                || candidate.Length >= data.Length;                                     // a patch that saves nothing
            if (checkpoint)
            {
                run.Instance.HistoryPatchCount = 0;
            }
            else
            {
                snapshot = null;
                patch = candidate;
                run.Instance.HistoryPatchCount = since!.Value + 1;
            }
        }

        db.History.Add(new WorkflowHistory
        {
            InstanceId = run.Instance.Id,
            WorkflowKey = run.Definition.Key,
            TransitionKey = transitionKey,
            FromState = string.IsNullOrEmpty(from) ? null : from,
            ToState = run.Instance.State,
            Trigger = trigger,
            ExecutedBy = run.User.AuditName,
            Input = input?.ToJsonString(),
            Data = snapshot,
            Patch = patch,
            DataVersion = run.Instance.DataVersion,
            Tasks = run.CurrentHopTasks.Count > 0 ? JsonSerializer.Serialize(run.CurrentHopTasks, WorkflowJson.Options) : null,
            StartedAt = startedAt,
            FinishedAt = Now,
        });
        run.CurrentHopTasks.Clear();
    }

    /// <summary>Refreshes <c>BusySince</c> while a claim runs, so Busy recovery never faults an instance whose tasks are merely slow.</summary>
    private WorkflowHeartbeat StartBusyHeartbeat(Guid instanceId) =>
        WorkflowHeartbeat.Start(scopeFactory, options.Value.BusyHeartbeatInterval, time, logger, (beatDb, now, ct) =>
            beatDb.Instances.Where(i => i.Id == instanceId && i.Status == WorkflowInstanceStatus.Busy)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.BusySince, (DateTime?)now), ct));

    private WorkflowJob NewJob(WorkflowInstance instance, WorkflowJobKind kind, string? transition, string? state, DateTime dueAt, string? payload = null, string? requestedBy = null) => new()
    {
        Id = db.NewId(),
        InstanceId = instance.Id,
        WorkflowKey = instance.WorkflowKey ?? string.Empty,
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

    private ExpressionScope Scope(Run run, JsonObject? input, JsonObject? data = null) => new()
    {
        Data = data ?? run.Data,
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Workflow instance {InstanceId} failed unexpectedly")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow task {Task} of instance {InstanceId} failed (attempt {Attempt}/{MaxAttempts})")]
    private static partial void LogTaskFailed(ILogger logger, Exception exception, string task, Guid instanceId, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Workflow event handler {Handler} failed for {EventType} of instance {InstanceId}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handler, WorkflowEventType eventType, Guid instanceId);
}
