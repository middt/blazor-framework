using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Schema;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>Authorization, transition availability, and response projection for workflow instances.</summary>
public sealed partial class WorkflowEngine
{
    private TransitionDefinition FindManualTransition(WorkflowDefinition definition, WorkflowInstance instance, string key, WorkflowUser user)
    {
        if (instance.IsTerminal)
        {
            throw new WorkflowConflictException($"The instance is {instance.Status}.");
        }

        var transition = AvailableIn(definition, instance.State).FirstOrDefault(t => t.Key == key && t.Trigger == TriggerType.Manual)
            ?? throw new WorkflowConflictException($"Transition '{key}' is not available in state '{instance.State}'.");

        var data = JsonPath.ParseObject(instance.Data);
        if (!CanExecute(transition, definition, instance, data, user))
        {
            throw new WorkflowForbiddenException($"You are not allowed to execute '{key}'.");
        }

        if (!IsRuleSatisfied(transition, definition, instance, data, user))
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
            return expressions.EvaluateCondition(transition.Rule, RuleScope(definition, instance, data, user));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRuleFailed(logger, ex, transition.Key, instance.Id);
            return false;
        }
    }

    private ExpressionScope RuleScope(WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user) => new()
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
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Availability rule of transition {Transition} failed for instance {InstanceId}")]
    private static partial void LogRuleFailed(ILogger logger, Exception exception, string transition, Guid instanceId);

    private static bool IsCancellation(WorkflowDefinition definition, TransitionDefinition transition) =>
        definition.FindState(transition.Target) is { IsFinal: true };

    private bool CanExecute(TransitionDefinition transition, WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user)
    {
        if (user.IsSystem)
        {
            return true;
        }

        if (transition.DenyRoles.Count > 0 && transition.DenyRoles.Any(r => MatchesRoleEntry(r, definition, instance, data, user)))
        {
            return false;
        }

        return transition.Roles.Count == 0 || transition.Roles.Any(r => MatchesRoleEntry(r, definition, instance, data, user));
    }

    private bool MatchesRoleEntry(string roleEntry, WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user)
    {
        if (roleEntry is "$starter" or "$owner")
        {
            return !string.IsNullOrEmpty(instance.StartedBy) &&
                string.Equals(instance.StartedBy, user.AuditName, StringComparison.Ordinal);
        }

        if (roleEntry == "$previousUser")
        {
            var prev = instance.LastExecutedBy ?? instance.StartedBy;
            return !string.IsNullOrEmpty(prev) &&
                string.Equals(prev, user.AuditName, StringComparison.Ordinal);
        }

        if (roleEntry.StartsWith("$user:", StringComparison.Ordinal))
        {
            var resolved = ResolveRoleExpression(roleEntry["$user:".Length..].Trim(), definition, instance, data, user);
            return !string.IsNullOrEmpty(resolved) &&
                string.Equals(resolved, user.AuditName, StringComparison.Ordinal);
        }

        if (roleEntry.StartsWith("$role:", StringComparison.Ordinal))
        {
            var resolved = ResolveRoleExpression(roleEntry["$role:".Length..].Trim(), definition, instance, data, user);
            return !string.IsNullOrEmpty(resolved) && user.IsInRole(resolved);
        }

        return user.IsInRole(roleEntry);
    }

    private string? ResolveRoleExpression(string exprOrTemplate, WorkflowDefinition definition, WorkflowInstance instance, JsonObject data, WorkflowUser user)
    {
        try
        {
            var scope = RuleScope(definition, instance, data, user);
            return exprOrTemplate.Contains("{{", StringComparison.Ordinal)
                ? expressions.RenderText(exprOrTemplate, scope).Trim()
                : expressions.Evaluate(exprOrTemplate, scope)?.ToString()?.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRuleFailed(logger, ex, exprOrTemplate, instance.Id);
            return null;
        }
    }

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
        var activeSubFlow = instance.WaitingForInstanceId is { } waitingId
            ? run.ActiveSubFlowModel ?? await LoadActiveSubFlowAsync(waitingId, run.User, cancellationToken)
            : null;
        return ToModel(instance, run.User, scheduled, activeSubFlow);
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

        var activeSubFlow = instance.WaitingForInstanceId is { } waitingId
            ? await LoadActiveSubFlowAsync(waitingId, user, cancellationToken)
            : null;
        return ToModel(instance, user, scheduled, activeSubFlow);
    }

    private async Task<WorkflowInstanceModel?> LoadActiveSubFlowAsync(Guid childId, WorkflowUser user, CancellationToken cancellationToken)
    {
        var child = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == childId, cancellationToken);
        return child is null ? null : await ToModelAsync(child, user, cancellationToken);
    }

    private WorkflowInstanceModel ToModel(
        WorkflowInstance instance,
        WorkflowUser user,
        List<ScheduledTransitionModel> scheduled,
        WorkflowInstanceModel? activeSubFlow = null)
    {
        var definition = registry.Get(instance.WorkflowKey, instance.WorkflowVersion);
        var state = definition.FindState(instance.State);

        var data = JsonPath.ParseObject(instance.Data);
        var available = instance.Status == WorkflowInstanceStatus.Active
            ? AvailableIn(definition, instance.State)
                .Where(t => t.Trigger == TriggerType.Manual && CanExecute(t, definition, instance, data, user))
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
                    InputFormat = t.Input?.Format == InputFormat.JsonPatch ? "jsonPatch" : null,
                })
                .ToList()
            : [];

        var projectedData = JsonSchemaValidator.ProjectData(data, definition.DataSchema, user);

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
            Data = projectedData,
            DataVersion = instance.DataVersion,
            ParentInstanceId = instance.ParentInstanceId,
            WaitingForInstanceId = instance.WaitingForInstanceId,
            ActiveSubFlow = activeSubFlow,
            Error = instance.Error is null ? null : options.Value.ExposeErrorDetails ? instance.Error : HiddenErrorText,
            StartedBy = instance.StartedBy,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
            AvailableTransitions = available,
            ScheduledTransitions = scheduled,
        };
    }
}
