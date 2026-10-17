using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// A unit of work run on state entry/exit or transition execution. Modify <see cref="WorkflowTaskContext.Data"/> to change
/// instance data; throw <see cref="Runtime.WorkflowTaskException"/> (or any exception) to fail and trigger the error policy.
/// Tasks may run more than once (retries, retry endpoint), so keep side effects idempotent.
/// </summary>
public interface IWorkflowTask
{
    /// <summary>Validates the task's configuration when definitions are loaded; return error messages.</summary>
    IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) => [];

    Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken);
}

public sealed class WorkflowTaskContext
{
    public required TaskDefinition Task { get; init; }

    /// <summary>Mutable instance data.</summary>
    public required JsonObject Data { get; init; }

    public required ExpressionScope Scope { get; init; }

    public required WorkflowExpressionEngine Expressions { get; init; }

    public required Guid InstanceId { get; init; }

    /// <summary>Stable across attempts of this task execution; send it to external systems that support idempotency.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    public required string Workflow { get; init; }

    public required string State { get; init; }

    public required IServiceProvider Services { get; init; }

    public required ILogger Logger { get; init; }

    public JsonObject Config => Task.Config;

    /// <summary>Renders a config value (resolving <c>{{ expression }}</c> placeholders against the current data).</summary>
    public JsonNode? Render(string configKey) => Expressions.Render(Config[configKey], Scope);

    public string? RenderText(string configKey) =>
        Config[configKey]?.GetValue<string>() is { } text ? Expressions.RenderText(text, Scope) : null;

    /// <summary>Business key of the instance when the task started (<c>null</c> when it has none).</summary>
    public string? BusinessKey { get; init; }

    /// <summary>Sets the instance's business key (wired by the engine, see the <c>setKey</c> task).</summary>
    internal Func<string, CancellationToken, Task>? SetBusinessKey { get; init; }

    /// <summary>Starts another workflow (wired by the engine).</summary>
    internal Func<string, JsonObject, string?, CancellationToken, Task<Guid>>? StartWorkflow { get; init; }
}

/// <summary>Publishes workflow messages (<c>publish</c> task); register an implementation, e.g. on Rebus.</summary>
public interface IWorkflowMessagePublisher
{
    Task PublishAsync(string messageType, JsonObject payload, CancellationToken cancellationToken);
}
