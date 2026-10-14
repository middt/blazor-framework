using System.Text.Json.Nodes;

namespace Middt.Framework.Workflow.Expressions;

/// <summary>Values available to expressions: <c>data</c>, <c>input</c>, <c>result</c>, <c>child</c>, <c>user</c>, <c>instance</c>, <c>now</c>.</summary>
public sealed class ExpressionScope
{
    public JsonObject Data { get; init; } = [];

    /// <summary>Request body of the current transition.</summary>
    public JsonObject? Input { get; init; }

    /// <summary>Response of the task being mapped (e.g. HTTP response body).</summary>
    public JsonNode? Result { get; init; }

    /// <summary>Finished subflow (<c>child.state</c>, <c>child.subType</c>, <c>child.data</c>) while a parent resumes.</summary>
    public JsonObject? Child { get; init; }

    public WorkflowUser User { get; init; } = WorkflowUser.System;

    /// <summary><c>instance.id</c>, <c>instance.key</c>, <c>instance.state</c>, <c>instance.workflow</c>.</summary>
    public JsonObject Instance { get; init; } = [];

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}
