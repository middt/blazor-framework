using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Workflow.Tasks;

/// <summary><c>set</c>: <c>{ "values": { "path": "expression" } }</c> – computes values into the instance data.</summary>
public sealed class SetTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["values"] is not JsonObject values || values.Count == 0)
        {
            yield return "set: 'values' object is required.";
            yield break;
        }

        foreach (var (path, expression) in values)
        {
            if (expression?.GetValue<string>() is not { } text || expressions.Validate(text) is { } error)
            {
                yield return $"set: value of '{path}' is not a valid expression.";
            }
        }
    }

    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        foreach (var (path, expression) in (JsonObject)context.Config["values"]!)
        {
            JsonPath.Set(context.Data, path, context.Expressions.EvaluateToNode(expression!.GetValue<string>(), context.Scope));
        }

        return Task.CompletedTask;
    }
}

/// <summary><c>log</c>: <c>{ "message": "Order {{ data.orderId }} approved", "level": "Information" }</c>.</summary>
public sealed partial class LogTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        task.Config["message"] is null ? ["log: 'message' is required."] : expressions.ValidateTemplate(task.Config["message"]);

    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var level = Enum.TryParse<LogLevel>(context.Config["level"]?.GetValue<string>(), out var parsed) ? parsed : LogLevel.Information;
        LogMessage(context.Logger, level, context.Workflow, context.InstanceId, context.RenderText("message"));
        return Task.CompletedTask;
    }

    [LoggerMessage(Message = "[{Workflow} {InstanceId}] {Message}")]
    private static partial void LogMessage(ILogger logger, LogLevel level, string workflow, Guid instanceId, string? message);
}



/// <summary><c>publish</c>: <c>{ "type": "OrderApproved", "payload": { ... } }</c> via <see cref="IWorkflowMessagePublisher"/>.</summary>
public sealed class PublishTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["type"]?.GetValue<string>() is not { Length: > 0 })
        {
            yield return "publish: 'type' is required.";
        }

        foreach (var error in expressions.ValidateTemplate(task.Config["payload"]))
        {
            yield return "publish: " + error;
        }
    }

    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var publisher = context.Services.GetService<IWorkflowMessagePublisher>()
            ?? throw new InvalidOperationException("publish task requires an IWorkflowMessagePublisher registration.");

        var payload = context.Render("payload") as JsonObject ?? [];
        payload["workflow"] ??= context.Workflow;
        payload["instanceId"] ??= context.InstanceId;
        return publisher.PublishAsync(context.Config["type"]!.GetValue<string>(), payload, cancellationToken);
    }
}

/// <summary>
/// <c>startWorkflow</c>: starts another workflow without waiting for it (sub-process).
/// <code>{ "workflow": "shipping", "key": "ship-{{ data.orderId }}", "data": { "orderId": "{{ data.orderId }}" }, "into": "shippingInstanceId" }</code>
/// </summary>
public sealed class StartWorkflowTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["workflow"]?.GetValue<string>() is not { Length: > 0 })
        {
            yield return "startWorkflow: 'workflow' is required.";
        }

        foreach (var error in expressions.ValidateTemplate(task.Config["data"]).Concat(expressions.ValidateTemplate(task.Config["key"])))
        {
            yield return "startWorkflow: " + error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var data = context.Render("data") as JsonObject ?? [];
        var key = context.RenderText("key");
        var id = await context.StartWorkflow!(context.Config["workflow"]!.GetValue<string>(), data, string.IsNullOrEmpty(key) ? null : key, cancellationToken);

        if (context.Config["into"]?.GetValue<string>() is { Length: > 0 } into)
        {
            JsonPath.Set(context.Data, into, JsonValue.Create(id));
        }
    }
}

/// <summary>
/// <c>setKey</c>: gives the instance its business key once it is known, e.g. after the record was created:
/// <code>{ "key": "category-{{ data.categoryId }}" }</code>
/// Starting the workflow with that key later returns this instance (idempotent start), so one record gets one instance.
/// The same key again is a no-op; a different existing key, an empty key or a key used by another instance of the same
/// workflow fails the task.
/// </summary>
public sealed class SetKeyTask : IWorkflowTask
{
    /// <summary>Column length of the business key.</summary>
    public const int MaxKeyLength = 256;

    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["key"] is not JsonValue key || !key.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            yield return "setKey: 'key' (text template) is required.";
            yield break;
        }

        foreach (var error in expressions.ValidateTemplate(key))
        {
            yield return "setKey: " + error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var key = context.RenderText("key")?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new WorkflowTaskException("setKey: the key is empty.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new WorkflowTaskException($"setKey: the key is longer than {MaxKeyLength} characters.");
        }

        if (context.SetBusinessKey is not { } setKey)
        {
            throw new InvalidOperationException("setKey runs inside the workflow engine only.");
        }

        if (context.Config["ensureUnique"]?.GetValue<bool>() == true)
        {
            if (!string.IsNullOrEmpty(context.BusinessKey))
            {
                return;
            }

            var db = context.Services.GetRequiredService<Persistence.WorkflowDbContext>();
            var exists = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Instances),
                i => i.WorkflowKey == context.Workflow && i.BusinessKey == key && i.Id != context.InstanceId,
                cancellationToken);
            if (exists)
            {
                var suffix = "-" + context.InstanceId.ToString("N")[..6].ToUpperInvariant();
                key = (key.Length + suffix.Length <= MaxKeyLength ? key : key[..(MaxKeyLength - suffix.Length)]) + suffix;
            }
        }

        await setKey(key, cancellationToken);
    }
}

/// <summary>
/// <c>loadWorkflow</c>: loads another workflow instance's JSON <c>Data</c> (and metadata) from <see cref="Persistence.WorkflowDbContext"/>
/// by business <c>key</c> or instance <c>id</c> into the current instance's data:
/// <code>{ "workflow": "customer-onboarding", "key": "{{ data.customerKey }}", "into": "customer" }</code>
/// </summary>
public sealed class LoadWorkflowTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["workflow"]?.GetValue<string>() is not { Length: > 0 })
        {
            yield return "loadWorkflow: 'workflow' is required.";
        }

        var hasKey = task.Config["key"]?.GetValue<string>() is { Length: > 0 };
        var hasId = task.Config["id"]?.GetValue<string>() is { Length: > 0 };
        if (!hasKey && !hasId)
        {
            yield return "loadWorkflow: either 'key' or 'id' is required.";
        }

        if (task.Config["into"]?.GetValue<string>() is not { Length: > 0 })
        {
            yield return "loadWorkflow: 'into' is required.";
        }

        foreach (var error in expressions.ValidateTemplate(task.Config["key"]).Concat(expressions.ValidateTemplate(task.Config["id"])))
        {
            yield return "loadWorkflow: " + error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<Persistence.WorkflowDbContext>();
        var workflow = context.Config["workflow"]!.GetValue<string>();
        var into = context.Config["into"]!.GetValue<string>();
        var key = context.RenderText("key")?.Trim();
        var idText = context.RenderText("id")?.Trim();

        Persistence.WorkflowInstance? instance = null;
        if (Guid.TryParse(idText, out var id))
        {
            instance = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Instances),
                i => i.Id == id && i.WorkflowKey == workflow,
                cancellationToken);
        }
        else if (!string.IsNullOrEmpty(key))
        {
            instance = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Instances),
                i => i.WorkflowKey == workflow && i.BusinessKey == key,
                cancellationToken);
        }

        if (instance is null)
        {
            JsonPath.Set(context.Data, into, new JsonObject
            {
                ["id"] = idText,
                ["key"] = key,
                ["found"] = false,
            });
            return;
        }

        var loaded = JsonNode.Parse(string.IsNullOrWhiteSpace(instance.Data) ? "{}" : instance.Data) as JsonObject ?? [];
        loaded["id"] = instance.Id.ToString("D");
        loaded["key"] = instance.BusinessKey;
        loaded["state"] = instance.State;
        loaded["status"] = instance.Status.ToString();
        loaded["found"] = true;
        JsonPath.Set(context.Data, into, loaded);
    }
}

