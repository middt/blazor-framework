using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// <c>"background": true</c> onEntry tasks. Entering the state commits it with <see cref="WorkflowInstance.BackgroundTaskId"/>
/// set and a <see cref="WorkflowJobKind.Task"/> job; the instance stays Active (readable, other transitions allowed) while:
/// <list type="number">
/// <item>the job runs the task outside any claim, on a copy of the data, and stores the outcome (data patch, executed
/// tasks, error) in its payload – so a busy instance or a crash after this point never runs the task twice;</item>
/// <item>the job claims the instance and, if it is still waiting for this job, merges the patch, records a
/// <c>$background</c> history row, runs the remaining onEntry tasks and the automatic transitions – or applies the task's
/// error policy. A result for a state that was left meanwhile is discarded.</item>
/// </list>
/// </summary>
public sealed partial class WorkflowEngine
{
    private const string BackgroundTrigger = "Background";

    private async Task<JobOutcome> ProcessBackgroundJobAsync(WorkflowJob job, WorkflowInstance instance, CancellationToken cancellationToken)
    {
        if (instance.BackgroundTaskId != job.Id)
        {
            return JobOutcome.Done; // the state was left (or re-entered): this result is no longer wanted
        }

        var payload = JsonPath.ParseObject(job.Payload);
        if (payload["result"] is not JsonObject)
        {
            // Own scope: the task's incidents/entities never mix with the claim below.
            await using var scope = scopeFactory.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();
            payload["result"] = await worker.ExecuteBackgroundTaskAsync(job, instance, payload["taskIndex"]!.GetValue<int>(), cancellationToken);
            var text = payload.ToJsonString();
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.Payload, text), cancellationToken);
        }

        try
        {
            await ApplyBackgroundResultAsync(job, instance.Id, payload, cancellationToken);
            return JobOutcome.Done;
        }
        catch (WorkflowClaimException)
        {
            return JobOutcome.RetrySoon; // a transition is running; the stored result is applied afterwards
        }
    }

    private async Task<JsonObject> ExecuteBackgroundTaskAsync(WorkflowJob job, WorkflowInstance instance, int taskIndex, CancellationToken cancellationToken)
    {
        var definition = registry.Get(instance.WorkflowKey, instance.WorkflowVersion);
        var state = definition.FindState(instance.State)!;
        var task = state.OnEntry[taskIndex];
        var before = JsonPath.ParseObject(instance.Data);
        var run = new Run(definition, instance, (JsonObject)before.DeepClone(), new WorkflowUser { Name = job.RequestedBy ?? "system", IsSystem = true })
        {
            IdempotencyScope = job.Id.ToString("N"),
        };

        try
        {
            await RunTasksAsync(run, [task], job.TransitionKey ?? BackgroundTransition, "background", state.OnError, null, cancellationToken, taskIndex);
            return new JsonObject
            {
                ["status"] = "completed",
                ["patch"] = JsonPatch.Diff(before, run.Data),
                ["tasks"] = JsonSerializer.SerializeToNode(run.CurrentHopTasks, WorkflowJson.Options),
                ["continued"] = new JsonArray([.. run.ContinuedErrors.Select(e => (JsonNode)new JsonObject
                {
                    ["taskKey"] = e.TaskKey,
                    ["error"] = e.Error,
                    ["attempts"] = e.Attempts,
                })]),
            };
        }
        catch (TaskFailure failure)
        {
            return new JsonObject
            {
                ["status"] = "failed",
                ["taskKey"] = failure.Task.DisplayKey,
                ["error"] = failure.InnerException?.Message ?? failure.Message,
                ["attempts"] = failure.Attempts,
                ["action"] = failure.Policy.Action.ToString(),
                ["transition"] = failure.Policy.Transition,
            };
        }
    }

    private async Task ApplyBackgroundResultAsync(WorkflowJob job, Guid instanceId, JsonObject payload, CancellationToken cancellationToken)
    {
        var actor = new WorkflowUser { Name = job.RequestedBy ?? "system", IsSystem = true };
        var taskIndex = payload["taskIndex"]!.GetValue<int>();
        var result = (JsonObject)payload["result"]!;
        await RunClaimedAsync(instanceId, WorkflowInstanceStatus.Active, actor, async run =>
        {
            if (run.Instance.BackgroundTaskId != job.Id)
            {
                return;
            }

            run.Instance.BackgroundTaskId = null;
            var state = run.Definition.FindState(run.Instance.State)!;
            var startedAt = Now;

            if (result["status"]?.GetValue<string>() == "completed")
            {
                JsonPatch.Apply(run.Data, (JsonArray)result["patch"]!);
                foreach (var continued in result["continued"]?.AsArray() ?? [])
                {
                    AddIncident(run, BackgroundTransition, continued!["taskKey"]!.GetValue<string>(), continued["error"]!.GetValue<string>(), continued["attempts"]!.GetValue<int>());
                }

                run.CurrentHopTasks.AddRange(result["tasks"]?.Deserialize<List<WorkflowTaskExecutionModel>>(WorkflowJson.Options) ?? []);
                AddHistory(run, BackgroundTransition, run.Instance.State, BackgroundTrigger, null, startedAt);
                await ContinueOnEntryAsync(run, state, taskIndex + 1, job.TransitionKey, cancellationToken);
                return;
            }

            var taskKey = result["taskKey"]?.GetValue<string>();
            var error = result["error"]?.GetValue<string>() ?? "Background task failed.";
            AddIncident(run, BackgroundTransition, taskKey, error, result["attempts"]?.GetValue<int>() ?? 1);
            if (result["action"]?.GetValue<string>() == nameof(ErrorAction.Transition)
                && result["transition"]?.GetValue<string>() is { } errorKey
                && ResolveTransition(run.Definition, run.Instance, errorKey) is { } errorTransition)
            {
                await ExecuteChainAsync(run, errorTransition, null, "Error", cancellationToken);
                return;
            }

            FaultBackground(run, taskIndex, resume: false, error);
        }, cancellationToken);
    }

    /// <summary>Runs the onEntry tasks after a finished background task, then the automatic transitions.</summary>
    private async Task ContinueOnEntryAsync(Run run, StateDefinition state, int nextIndex, string? transitionKey, CancellationToken cancellationToken)
    {
        var remaining = state.OnEntry.Skip(nextIndex).ToList();
        var dataBefore = (JsonObject)run.Data.DeepClone();
        try
        {
            await RunTasksAsync(run, remaining, transitionKey ?? BackgroundTransition, "onEntry", state.OnError, null, cancellationToken, nextIndex);
        }
        catch (TaskFailure failure)
        {
            run.Data = dataBefore;
            run.NewJobs.RemoveAll(j => j.Kind == WorkflowJobKind.Task);
            run.Instance.BackgroundTaskId = null;
            var error = failure.InnerException?.Message ?? failure.Message;
            AddIncident(run, BackgroundTransition, failure.Task.DisplayKey, error, failure.Attempts);
            if (failure.Policy.Action == ErrorAction.Transition && ResolveTransition(run.Definition, run.Instance, failure.Policy.Transition!) is { } errorTransition)
            {
                await ExecuteChainAsync(run, errorTransition, null, "Error", cancellationToken);
                return;
            }

            FaultBackground(run, nextIndex, resume: true, error);
            return;
        }

        if (run.Instance.BackgroundTaskId is not null || run.Instance.Status != WorkflowInstanceStatus.Busy)
        {
            return; // another background task took over, or a task changed the status
        }

        TransitionDefinition? next;
        try
        {
            next = FindAutomatic(run);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FaultBackground(run, nextIndex, resume: true, $"Evaluating automatic transitions failed: {ex.Message}");
            return;
        }

        if (next is not null)
        {
            await ExecuteChainAsync(run, next, null, nameof(TriggerType.Automatic), cancellationToken);
        }
    }

    /// <summary>
    /// Faults the instance with <c>$background</c> as the pending transition: <c>retry</c> then runs the background task
    /// again (<paramref name="resume"/> = false) or the onEntry tasks after it (<paramref name="resume"/> = true).
    /// </summary>
    private void FaultBackground(Run run, int taskIndex, bool resume, string error)
    {
        run.Instance.Status = WorkflowInstanceStatus.Faulted;
        run.Instance.PendingTransition = BackgroundTransition;
        run.Instance.PendingInput = new JsonObject { ["taskIndex"] = taskIndex, ["resume"] = resume }.ToJsonString();
        run.Instance.Error = error;
        run.Events.Add(Event(run, WorkflowEventType.Faulted, run.Instance.State, BackgroundTransition));
    }

    private async Task RetryBackgroundAsync(Run run, CancellationToken cancellationToken)
    {
        var pending = JsonPath.ParseObject(run.Instance.PendingInput);
        var taskIndex = pending["taskIndex"]?.GetValue<int>() ?? 0;
        var resume = pending["resume"]?.GetValue<bool>() == true;
        run.Instance.PendingTransition = null;
        run.Instance.PendingInput = null;
        run.Instance.Error = null;
        run.ResolveIncidents = true;

        var state = run.Definition.FindState(run.Instance.State)
            ?? throw new WorkflowConflictException($"State '{run.Instance.State}' no longer exists.");
        if (resume)
        {
            await ContinueOnEntryAsync(run, state, taskIndex, BackgroundTransition, cancellationToken);
            return;
        }

        var job = NewJob(run.Instance, WorkflowJobKind.Task, BackgroundTransition, run.Instance.State, Now,
            new JsonObject { ["taskIndex"] = taskIndex }.ToJsonString(), run.User.AuditName);
        run.NewJobs.Add(job);
        run.Instance.BackgroundTaskId = job.Id;
    }
}
