using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>cache</c>: caches the output of a nested workflow task by a rendered key and TTL using <see cref="IDistributedCache"/>
/// (when registered) or <see cref="IMemoryCache"/>.
/// <code>
/// {
///   "task": "cache",
///   "key": "fx-rate:{{ data.currency }}",
///   "ttl": "10m",
///   "into": "rate",
///   "subTask": { "task": "http", "url": "https://api.example.com/rates/{{ data.currency }}", "into": "rate" }
/// }
/// </code>
/// </summary>
public sealed class CacheTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["key"] is not JsonValue keyVal || !keyVal.TryGetValue<string>(out var keyText) || string.IsNullOrWhiteSpace(keyText))
        {
            yield return "cache: 'key' (text template) is required.";
        }
        else
        {
            foreach (var error in expressions.ValidateTemplate(keyVal))
            {
                yield return "cache: " + error;
            }
        }

        if (task.Config["ttl"]?.GetValue<string>() is { } ttl && !Durations.TryParse(ttl, out _))
        {
            yield return "cache: 'ttl' is not a valid duration.";
        }

        var innerNode = task.Config["task"] ?? task.Config["subTask"] ?? task.Config["innerTask"];
        if (innerNode is not JsonObject innerObj)
        {
            yield return "cache: 'task' (or 'subTask') object is required in config.";
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
            yield return $"cache: invalid 'subTask': {parseError}";
            yield break;
        }

        if (innerTask is null || string.IsNullOrWhiteSpace(innerTask.Task))
        {
            yield return "cache: 'subTask.task' is required.";
            yield break;
        }

        var into = task.Config["into"]?.GetValue<string>() ?? innerTask.Config["into"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(into))
        {
            yield return "cache: 'into' path is required (on the cache task or its subTask).";
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var rawKey = context.RenderText("key")?.Trim();
        if (string.IsNullOrEmpty(rawKey))
        {
            throw new WorkflowTaskException("cache: rendered 'key' is empty.");
        }

        var cacheKey = $"middt:wf:cache:{rawKey}";
        var ttlText = context.Config["ttl"]?.GetValue<string>() ?? "PT5M";
        var ttl = Durations.Parse(ttlText);

        var innerNode = (context.Config["task"] ?? context.Config["subTask"] ?? context.Config["innerTask"])!;
        var innerTask = innerNode.Deserialize<TaskDefinition>(WorkflowJson.Options)
            ?? throw new WorkflowTaskException("cache: 'subTask' could not be deserialized.");

        var into = context.Config["into"]?.GetValue<string>() ?? innerTask.Config["into"]?.GetValue<string>()
            ?? throw new WorkflowTaskException("cache: 'into' path is required.");

        var distributed = context.Services.GetService<IDistributedCache>();
        var memory = context.Services.GetService<IMemoryCache>();

        if (distributed is not null)
        {
            var cachedJson = await distributed.GetStringAsync(cacheKey, cancellationToken);
            if (cachedJson is not null)
            {
                JsonPath.Set(context.Data, into, JsonNode.Parse(cachedJson));
                return;
            }
        }
        else if (memory is not null && memory.TryGetValue<string>(cacheKey, out var cachedJson) && cachedJson is not null)
        {
            JsonPath.Set(context.Data, into, JsonNode.Parse(cachedJson));
            return;
        }

        var taskRegistry = context.Services.GetRequiredService<WorkflowTaskRegistry>();
        var impl = taskRegistry.Resolve(context.Services, innerTask.Task);
        var innerContext = new WorkflowTaskContext
        {
            Task = innerTask,
            Data = context.Data,
            Scope = context.Scope,
            Expressions = context.Expressions,
            InstanceId = context.InstanceId,
            IdempotencyKey = $"{context.IdempotencyKey}:cache",
            Workflow = context.Workflow,
            State = context.State,
            Services = context.Services,
            Logger = context.Logger,
            BusinessKey = context.BusinessKey,
            SetBusinessKey = context.SetBusinessKey,
            StartWorkflow = context.StartWorkflow,
        };

        await impl.ExecuteAsync(innerContext, cancellationToken);

        var sourcePath = innerTask.Config["into"]?.GetValue<string>() ?? into;
        var produced = JsonPath.Get(context.Data, sourcePath);
        if (!string.Equals(into, sourcePath, StringComparison.Ordinal))
        {
            JsonPath.Set(context.Data, into, produced?.DeepClone());
        }

        var serialized = produced?.ToJsonString() ?? "null";

        if (distributed is not null)
        {
            await distributed.SetStringAsync(
                cacheKey,
                serialized,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        else if (memory is not null)
        {
            memory.Set(cacheKey, serialized, ttl);
        }
    }
}
