using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Diagnostics;
using Middt.Framework.Workflow.Endpoints;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>A message for <c>"trigger": "Event"</c> transitions, correlated to one instance.</summary>
public sealed class WorkflowMessage
{
    /// <summary>Message name; matches <see cref="TransitionDefinition.Event"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Workflow key; required with <see cref="Key"/>, optional (checked) with <see cref="InstanceId"/>.</summary>
    public string? Workflow { get; init; }

    /// <summary>Target instance. Exactly one of <see cref="InstanceId"/> and <see cref="Key"/> correlates the message.</summary>
    public Guid? InstanceId { get; init; }

    /// <summary>Business key of the target instance within <see cref="Workflow"/>.</summary>
    public string? Key { get; init; }

    /// <summary>Message body: the transition input (validated by the transition's <c>input</c> schema).</summary>
    public JsonObject? Data { get; init; }

    /// <summary>
    /// How long to keep the message when the instance cannot take it yet (busy, faulted, or not in a state waiting for it).
    /// Zero (default) rejects it instead. A buffered message is delivered as soon as the instance enters a state with a
    /// matching event transition, and dropped when it expires.
    /// </summary>
    public TimeSpan BufferFor { get; init; }
}

public enum WorkflowMessageOutcome
{
    /// <summary>An event transition ran; <see cref="WorkflowMessageResult.Instance"/> is the instance afterwards.</summary>
    Delivered,

    /// <summary>The instance could not take the message yet; it is kept until <see cref="WorkflowMessage.BufferFor"/> elapses.</summary>
    Buffered,
}

public sealed record WorkflowMessageResult(WorkflowMessageOutcome Outcome, Guid InstanceId, WorkflowInstanceModel? Instance);

public sealed partial class WorkflowEngine
{
    internal const string EventTrigger = "Event";

    /// <summary>
    /// Delivers a message to the instance it correlates to: runs the first event transition of the current state (or a
    /// shared one) whose <see cref="TransitionDefinition.Event"/> is the message name and whose <c>rule</c> holds for the
    /// data and the message body (<c>input</c>). When the instance cannot take it now the message is buffered
    /// (<see cref="WorkflowMessage.BufferFor"/>) or rejected with a conflict.
    /// </summary>
    public async Task<WorkflowMessageResult> DeliverMessageAsync(WorkflowMessage message, WorkflowUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity($"workflow.message {message.Name}");
        activity?.SetTag("middt.workflow.message", message.Name);

        if (string.IsNullOrWhiteSpace(message.Name))
        {
            throw new WorkflowValidationException(["A message needs a name."]);
        }

        if (message.InstanceId is null == string.IsNullOrEmpty(message.Key))
        {
            throw new WorkflowValidationException(["Correlate a message with exactly one of instanceId or (workflow + key)."]);
        }

        if (message.BufferFor < TimeSpan.Zero)
        {
            throw new WorkflowValidationException(["bufferFor cannot be negative."]);
        }

        var snapshot = await FindMessageTargetAsync(message, user, cancellationToken);
        activity?.SetTag("middt.workflow.instance_id", snapshot.Id.ToString());
        var definition = registry.Get(snapshot.WorkflowKey, snapshot.WorkflowVersion);

        var listeners = definition.States.SelectMany(s => s.Transitions).Concat(definition.SharedTransitions)
            .Where(t => t.Trigger == TriggerType.Event && t.Event == message.Name)
            .ToList();
        if (listeners.Count == 0)
        {
            throw new WorkflowConflictException($"Workflow '{definition.Key}' has no transition for message '{message.Name}'.");
        }

        // Buffered messages run as the system later, so the caller's right to send it is checked now.
        var data = JsonPath.ParseObject(snapshot.Data);
        if (!listeners.Any(t => CanExecute(t, definition, snapshot, data, user)))
        {
            throw new WorkflowForbiddenException($"You are not allowed to send '{message.Name}'.");
        }

        var input = WorkflowHttpInput.EnsureEnvelope(message.Data);
        if (await TryDeliverAsync(snapshot, definition, message.Name, input, user, cancellationToken) is { } delivered)
        {
            activity?.SetTag("middt.workflow.message_outcome", nameof(WorkflowMessageOutcome.Delivered));
            return new WorkflowMessageResult(WorkflowMessageOutcome.Delivered, snapshot.Id, delivered);
        }

        if (message.BufferFor == TimeSpan.Zero)
        {
            throw new WorkflowConflictException(snapshot.IsTerminal
                ? $"The instance is {snapshot.Status}."
                : $"The instance cannot take '{message.Name}' in state '{snapshot.State}' ({snapshot.Status}).");
        }

        if (snapshot.IsTerminal)
        {
            throw new WorkflowConflictException($"The instance is {snapshot.Status}.");
        }

        var expiresAt = Now + message.BufferFor;
        var payload = new JsonObject
        {
            ["name"] = message.Name,
            ["data"] = message.Data?.DeepClone(),
            ["expiresAt"] = expiresAt,
        };

        // Busy: retry shortly (the claim ends soon). Otherwise wait until a matching state is entered, or expire.
        var dueAt = snapshot.Status == WorkflowInstanceStatus.Busy ? Now : expiresAt;
        db.Jobs.Add(NewJob(snapshot, WorkflowJobKind.Message, message.Name, null, dueAt, payload.ToJsonString(), user.AuditName));
        await db.SaveChangesAsync(cancellationToken);
        await jobNotifier.NotifyAsync(db, cancellationToken);

        activity?.SetTag("middt.workflow.message_outcome", nameof(WorkflowMessageOutcome.Buffered));
        return new WorkflowMessageResult(WorkflowMessageOutcome.Buffered, snapshot.Id, null);
    }

    private async Task<WorkflowInstance> FindMessageTargetAsync(WorkflowMessage message, WorkflowUser user, CancellationToken cancellationToken)
    {
        if (message.InstanceId is { } id)
        {
            return await FindAccessibleAsync(id, message.Workflow, user, cancellationToken);
        }

        if (string.IsNullOrEmpty(message.Workflow))
        {
            throw new WorkflowValidationException(["A message correlated by key needs the workflow."]);
        }

        var definition = registry.GetLatest(message.Workflow);
        var key = message.Key;
        var row = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.WorkflowKey == definition.Key && i.BusinessKey == key, cancellationToken)
            ?? throw new WorkflowNotFoundException($"No '{definition.Key}' instance has the key '{key}'.");
        EnsureCanAccess(registry.Get(row.WorkflowKey, row.WorkflowVersion), row, user);
        return row;
    }

    /// <summary>Runs the matching event transition, or returns <c>null</c> when the instance cannot take the message now.</summary>
    private async Task<WorkflowInstanceModel?> TryDeliverAsync(
        WorkflowInstance snapshot,
        WorkflowDefinition definition,
        string name,
        JsonObject input,
        WorkflowUser user,
        CancellationToken cancellationToken)
    {
        if (snapshot.Status != WorkflowInstanceStatus.Active)
        {
            return null;
        }

        var data = JsonPath.ParseObject(snapshot.Data);
        var transition = SelectEventTransition(definition, snapshot, data, name, input, user);
        if (transition is null)
        {
            return null;
        }

        if (snapshot.WaitingForInstanceId is not null && !IsCancellation(definition, transition))
        {
            return null;
        }

        ValidateInput(transition, input);
        try
        {
            var run = await RunClaimedAsync(snapshot.Id, WorkflowInstanceStatus.Active, user, run =>
                ExecuteChainAsync(run, transition, input, EventTrigger, cancellationToken),
                cancellationToken,
                (transition.Key, transition.Input?.Persistable(WorkflowHttpInput.ExtractBody(input, transition.Input?.Schema))),
                snapshot);
            return await ToModelAsync(run, cancellationToken);
        }
        catch (WorkflowClaimException)
        {
            return null; // busy or changed meanwhile: buffer / retry
        }
    }

    private TransitionDefinition? SelectEventTransition(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        JsonObject data,
        string name,
        JsonObject input,
        WorkflowUser user)
    {
        ExpressionScope? scope = null;
        foreach (var transition in AvailableIn(definition, instance.State).Where(t => t.Trigger == TriggerType.Event && t.Event == name))
        {
            if (!CanExecute(transition, definition, instance, data, user))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(transition.Rule))
            {
                return transition;
            }

            scope ??= new ExpressionScope
            {
                Data = data,
                Input = WorkflowHttpInput.ExtractBody(input, transition.Input?.Schema) ?? input,
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
            };

            try
            {
                if (expressions.EvaluateCondition(transition.Rule, scope))
                {
                    return transition;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRuleFailed(logger, ex, transition.Key, instance.Id);
            }
        }

        return null;
    }

    /// <summary>A buffered message (<see cref="WorkflowJobKind.Message"/>): deliver, keep waiting, or drop when expired.</summary>
    private async Task<JobOutcome> ProcessMessageJobAsync(WorkflowJob job, WorkflowInstance instance, CancellationToken cancellationToken)
    {
        var payload = JsonPath.ParseObject(job.Payload);
        var name = payload["name"]?.GetValue<string>() ?? job.TransitionKey ?? string.Empty;
        var expiresAt = payload["expiresAt"]?.GetValue<DateTime>() ?? Now;
        var input = WorkflowHttpInput.EnsureEnvelope(payload["data"] as JsonObject is { } body ? (JsonObject)body.DeepClone() : null);
        var definition = registry.Get(instance.WorkflowKey, instance.WorkflowVersion);
        var actor = new WorkflowUser { Name = job.RequestedBy ?? "system", IsSystem = true };

        if (!instance.IsTerminal && await TryDeliverAsync(instance, definition, name, input, actor, cancellationToken) is not null)
        {
            return JobOutcome.Done;
        }

        if (instance.IsTerminal || Now >= expiresAt)
        {
            LogMessageDropped(logger, name, instance.Id, instance.IsTerminal ? instance.Status.ToString() : "expired");
            return JobOutcome.Done;
        }

        if (instance.Status == WorkflowInstanceStatus.Busy)
        {
            return JobOutcome.RetrySoon;
        }

        // Not waiting for it now: park until a matching state is entered (SaveAsync wakes it) or it expires.
        await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.DueAt, expiresAt)
            .SetProperty(j => j.LockedUntil, (DateTime?)null)
            .SetProperty(j => j.Attempts, 0), cancellationToken);
        return JobOutcome.Rescheduled;
    }

    /// <summary>Event names the instance can take in <paramref name="state"/> (state and shared event transitions).</summary>
    private static List<string> EventsAvailableIn(WorkflowDefinition definition, string state) =>
        [.. AvailableIn(definition, state).Where(t => t.Trigger == TriggerType.Event && !string.IsNullOrEmpty(t.Event)).Select(t => t.Event!).Distinct(StringComparer.Ordinal)];

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {Message} for instance {InstanceId} dropped ({Reason})")]
    private static partial void LogMessageDropped(ILogger logger, string message, Guid instanceId, string reason);
}
