using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>Data passed to an application operation without exposing workflow engine internals.</summary>
public sealed class WorkflowOperationContext
{
    public required JsonObject Data { get; init; }

    /// <summary>The current transition input, including sensitive fields that are never persisted.</summary>
    public JsonObject? Input { get; init; }

    public required JsonObject Config { get; init; }

    /// <summary>Stable within the attempts of one workflow command.</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>Explicitly dispatches approved application operations invoked by the general <c>operation</c> task.</summary>
public interface IWorkflowOperationDispatcher
{
    bool CanHandle(string operation);

    Task ExecuteAsync(string operation, WorkflowOperationContext context, CancellationToken cancellationToken);
}

/// <summary>Invokes a registered application operation by name. The dispatcher owns its allowlist and domain logic.</summary>
public sealed class OperationTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        task.Config["operation"] is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? []
            : ["operation: 'operation' is required."];

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var name = context.Config["operation"]?.GetValue<string>()
            ?? throw new InvalidOperationException("The operation name is missing.");
        var dispatcher = context.Services.GetServices<IWorkflowOperationDispatcher>().SingleOrDefault(d => d.CanHandle(name))
            ?? throw new InvalidOperationException($"No dispatcher is registered for operation '{name}'.");
        await dispatcher.ExecuteAsync(name, new WorkflowOperationContext
        {
            Data = context.Data,
            Input = context.Scope.Input,
            Config = context.Config,
            IdempotencyKey = context.IdempotencyKey,
        }, cancellationToken);
    }
}
