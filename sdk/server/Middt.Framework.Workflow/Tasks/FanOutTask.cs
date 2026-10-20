using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Diagnostics;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>fanOut</c>: executes a nested workflow task across each element of an array expression with bounded concurrency.
/// <code>
/// {
///   "task": "fanOut",
///   "items": "data.items",
///   "maxConcurrency": 4,
///   "into": "results",
///   "subTask": { "task": "http", "url": "https://api.example.com/items/{{ item.sku }}", "into": "res" }
/// }
/// </code>
/// Supports either <c>"subTask"</c> or <c>"innerTask"</c> for the nested task definition.
/// Inside the nested task, <c>item</c> (object, or <c>{ "value": ... }</c> for primitives) and <c>index</c> (0-based int) are bound in expressions.
/// </summary>
public sealed class FanOutTask : IWorkflowTask
{
    public const int DefaultMaxConcurrency = 5;
    public const int MaxAllowedConcurrency = 64;

    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["items"]?.GetValue<string>() is not { Length: > 0 } itemsExpr)
        {
            yield return "fanOut: 'items' expression is required.";
        }
        else if (expressions.Validate(itemsExpr) is { } err)
        {
            yield return $"fanOut: items: {err}";
        }

        if (task.Config["maxConcurrency"] is JsonValue mcVal)
        {
            if (!mcVal.TryGetValue<int>(out var mc) || mc < 1 || mc > MaxAllowedConcurrency)
            {
                yield return $"fanOut: 'maxConcurrency' must be an integer between 1 and {MaxAllowedConcurrency}.";
            }
        }

        var innerNode = task.Config["task"] ?? task.Config["subTask"] ?? task.Config["innerTask"];
        if (innerNode is not JsonObject innerObj)
        {
            yield return "fanOut: 'task' (or 'subTask') object is required in config.";
            yield break;
        }

        TaskDefinition? innerTask = null;
        string? parseError = null;
        try
        {
            innerTask = innerObj.Deserialize<TaskDefinition>(WorkflowJson.Options);
        }
        catch (JsonException ex)
        {
            parseError = ex.Message;
        }

        if (parseError is not null)
        {
            yield return $"fanOut: invalid 'subTask': {parseError}";
            yield break;
        }

        if (innerTask is null || string.IsNullOrWhiteSpace(innerTask.Task))
        {
            yield return "fanOut: 'subTask.task' is required.";
        }
        else if (!string.IsNullOrWhiteSpace(innerTask.When) && expressions.Validate(innerTask.When) is { } whenErr)
        {
            yield return $"fanOut: subTask.when: {whenErr}";
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var itemsExpr = context.Config["items"]!.GetValue<string>();
        var evaluated = context.Expressions.EvaluateToNode(itemsExpr, context.Scope);
        if (evaluated is null)
        {
            if (context.Config["into"]?.GetValue<string>() is { Length: > 0 } emptyInto)
            {
                JsonPath.Set(context.Data, emptyInto, new JsonArray());
            }

            return;
        }

        if (evaluated is not JsonArray itemsArray)
        {
            throw new WorkflowTaskException("fanOut: 'items' expression did not evaluate to an array.");
        }

        var innerNode = (context.Config["task"] ?? context.Config["subTask"] ?? context.Config["innerTask"])!;
        var innerTask = innerNode.Deserialize<TaskDefinition>(WorkflowJson.Options)
            ?? throw new WorkflowTaskException("fanOut: 'subTask' could not be deserialized.");

        var taskRegistry = context.Services.GetRequiredService<WorkflowTaskRegistry>();
        var maxConcurrency = context.Config["maxConcurrency"] is JsonValue mcVal && mcVal.TryGetValue<int>(out var mc)
            ? Math.Clamp(mc, 1, MaxAllowedConcurrency)
            : DefaultMaxConcurrency;

        var into = context.Config["into"]?.GetValue<string>();
        var innerInto = innerTask.Config["into"]?.GetValue<string>();
        var count = itemsArray.Count;
        var itemResults = new JsonNode?[count];
        var itemDeltas = new JsonObject?[count];

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new Task[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var rawElement = itemsArray[index]?.DeepClone();
            var itemObject = rawElement is JsonObject obj
                ? (JsonObject)obj.DeepClone()
                : new JsonObject { ["value"] = rawElement?.DeepClone() };
            itemObject["index"] ??= index;
            var itemData = (JsonObject)context.Data.DeepClone();

            tasks[index] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var itemScope = new ExpressionScope
                    {
                        Data = itemData,
                        Input = context.Scope.Input,
                        Result = context.Scope.Result,
                        Child = context.Scope.Child,
                        Instance = context.Scope.Instance,
                        Item = itemObject,
                        Index = index,
                        User = context.Scope.User,
                        Now = context.Scope.Now,
                    };

                    if (!string.IsNullOrWhiteSpace(innerTask.When) &&
                        !context.Expressions.EvaluateCondition(innerTask.When, itemScope))
                    {
                        return;
                    }

                    var impl = taskRegistry.Resolve(context.Services, innerTask.Task);
                    var itemContext = new WorkflowTaskContext
                    {
                        Task = innerTask,
                        Data = itemData,
                        Scope = itemScope,
                        Expressions = context.Expressions,
                        InstanceId = context.InstanceId,
                        IdempotencyKey = $"{context.IdempotencyKey}:fanout:{index}",
                        Workflow = context.Workflow,
                        State = context.State,
                        Services = context.Services,
                        Logger = context.Logger,
                        BusinessKey = context.BusinessKey,
                        SetBusinessKey = context.SetBusinessKey,
                        StartWorkflow = context.StartWorkflow,
                    };

                    var sw = Stopwatch.StartNew();
                    using var itemActivity = WorkflowDiagnostics.StartTaskActivity(
                        context.Workflow,
                        context.InstanceId,
                        "fanOut",
                        $"fanOut[{index}]",
                        $"{innerTask.DisplayKey}[{index}]",
                        innerTask.Task,
                        1);
                    itemActivity?.SetTag("middt.workflow.fanout.index", index);

                    try
                    {
                        await impl.ExecuteAsync(itemContext, cancellationToken);
                        WorkflowDiagnostics.CompleteTaskActivity(itemActivity, context.Workflow, innerTask.Task, "fanOut", "Completed", sw.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        WorkflowDiagnostics.CompleteTaskActivity(itemActivity, context.Workflow, innerTask.Task, "fanOut", "Failed", sw.Elapsed.TotalMilliseconds, ex.Message);
                        throw;
                    }

                    if (!string.IsNullOrEmpty(innerInto))
                    {
                        itemResults[index] = JsonPath.Get(itemData, innerInto)?.DeepClone();
                    }
                    else
                    {
                        var delta = new JsonObject();
                        foreach (var (k, v) in itemData)
                        {
                            if (!context.Data.TryGetPropertyValue(k, out var orig) || !JsonNode.DeepEquals(orig, v))
                            {
                                delta[k] = v?.DeepClone();
                            }
                        }

                        itemDeltas[index] = delta;
                        itemResults[index] = delta.Count == 1 && delta.TryGetPropertyValue("result", out var singleResult)
                            ? singleResult?.DeepClone()
                            : delta.DeepClone();
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);

        if (!string.IsNullOrEmpty(into))
        {
            var array = new JsonArray();
            for (var i = 0; i < count; i++)
            {
                array.Add(itemResults[i]?.DeepClone());
            }

            JsonPath.Set(context.Data, into, array);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                if (itemDeltas[i] is { Count: > 0 } delta)
                {
                    JsonPath.Merge(context.Data, delta);
                }
            }
        }
    }
}
