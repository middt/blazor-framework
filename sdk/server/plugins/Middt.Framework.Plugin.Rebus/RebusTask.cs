using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;
using Rebus.Bus;

namespace Middt.Framework.Plugin.Rebus;

/// <summary>
/// <c>rebus</c>: executes messaging operations using Rebus (<see cref="IBus"/>).
/// <para>
/// Supported actions:
/// <list type="bullet">
///   <item><description><c>publish</c>: Publishes an event to all subscribers or a topic (<see cref="IBus.Publish"/> / Topics.Publish).</description></item>
///   <item><description><c>send</c>: Sends a command to a destination queue or routed queue (<see cref="IBus.Send"/> / Routing.Send).</description></item>
///   <item><description><c>sendLocal</c>: Sends a command to the service's own input queue (<see cref="IBus.SendLocal"/>).</description></item>
///   <item><description><c>defer</c>: Schedules a command to be delivered after a delay (<see cref="IBus.Defer"/> / Routing.Defer).</description></item>
///   <item><description><c>deferLocal</c>: Schedules a command to the local queue after a delay (<see cref="IBus.DeferLocal"/>).</description></item>
///   <item><description><c>subscribe</c>: Dynamically subscribes to an event type or topic (<see cref="IBus.Subscribe"/> / Topics.Subscribe).</description></item>
///   <item><description><c>unsubscribe</c>: Dynamically unsubscribes from an event type or topic (<see cref="IBus.Unsubscribe"/> / Topics.Unsubscribe).</description></item>
/// </list>
/// </para>
/// <code>
/// {
///   "action": "send",
///   "queue": "billing",
///   "type": "CreateInvoice",
///   "payload": { "orderId": "{{ data.orderId }}", "total": "{{ data.total }}" },
///   "headers": { "correlationId": "{{ data.correlationId }}" },
///   "into": "msgResult"
/// }
/// </code>
/// </summary>
public sealed class RebusTask : IWorkflowTask
{
    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        var config = task.Config;
        var action = DetermineAction(task);

        if (action is "defer" or "deferLocal" or "defer-local")
        {
            var hasDelay = config["delay"] is not null || config["deferSeconds"] is not null || config["delaySeconds"] is not null;
            if (!hasDelay)
            {
                yield return "rebus: 'delay' (e.g. '00:05:00', '10m', '30s') or 'deferSeconds' is required for defer actions.";
            }
        }

        if (action is "subscribe" or "unsubscribe")
        {
            var hasTopicOrType = config["topic"] is not null || config["type"] is not null || config["messageType"] is not null;
            if (!hasTopicOrType)
            {
                yield return "rebus: 'topic' or 'type' is required for subscribe/unsubscribe actions.";
            }
        }
        else
        {
            var hasPayload = config["payload"] is not null || config["message"] is not null || config["body"] is not null;
            if (!hasPayload)
            {
                yield return "rebus: 'payload' (or 'message' / 'body') is required.";
            }
        }

        foreach (var error in expressions.ValidateTemplate(config["type"])
                     .Concat(expressions.ValidateTemplate(config["messageType"]))
                     .Concat(expressions.ValidateTemplate(config["topic"]))
                     .Concat(expressions.ValidateTemplate(config["queue"]))
                     .Concat(expressions.ValidateTemplate(config["destination"]))
                     .Concat(expressions.ValidateTemplate(config["delay"]))
                     .Concat(expressions.ValidateTemplate(config["payload"]))
                     .Concat(expressions.ValidateTemplate(config["message"]))
                     .Concat(expressions.ValidateTemplate(config["body"]))
                     .Concat(expressions.ValidateTemplate(config["headers"])))
        {
            yield return "rebus: " + error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var bus = context.Services.GetService<IBus>()
            ?? throw new WorkflowTaskException("rebus task requires an IBus registration (call services.AddMiddtRebus(...)).");

        var config = context.Config;
        var action = DetermineAction(context);

        var destination = context.RenderText("destination") ?? context.RenderText("queue");
        var topic = context.RenderText("topic");
        var messageType = context.RenderText("type") ?? context.RenderText("messageType");

        var payloadNode = context.Render("payload")
                          ?? context.Render("message")
                          ?? context.Render("body");

        var headersNode = context.Render("headers") as JsonObject;
        var headers = new Dictionary<string, string>();
        if (headersNode is not null)
        {
            foreach (var (k, v) in headersNode)
            {
                if (v is not null)
                {
                    headers[k] = v switch
                    {
                        JsonValue jv when jv.TryGetValue<string>(out var str) => str,
                        JsonValue jv => jv.ToString(),
                        _ => v.ToJsonString()
                    };
                }
            }
        }

        // Correlation ID propagation
        if (!headers.ContainsKey("rbs2-corr-id"))
        {
            if (headers.TryGetValue("correlationId", out var corrId) && !string.IsNullOrEmpty(corrId))
            {
                headers["rbs2-corr-id"] = corrId;
            }
            else
            {
                headers["rbs2-corr-id"] = context.InstanceId.ToString();
            }
        }

        var delay = ResolveDelay(config, context);

        switch (action.ToLowerInvariant())
        {
            case "subscribe":
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    await bus.Advanced.Topics.Subscribe(topic);
                }
                else if (ResolveClrType(messageType) is { } subType)
                {
                    await bus.Subscribe(subType);
                }
                else
                {
                    throw new WorkflowTaskException($"rebus subscribe: topic or known CLR type could not be resolved from '{messageType}'.");
                }
                break;

            case "unsubscribe":
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    await bus.Advanced.Topics.Unsubscribe(topic);
                }
                else if (ResolveClrType(messageType) is { } unsubType)
                {
                    await bus.Unsubscribe(unsubType);
                }
                else
                {
                    throw new WorkflowTaskException($"rebus unsubscribe: topic or known CLR type could not be resolved from '{messageType}'.");
                }
                break;

            case "publish":
                var pubMsg = PrepareMessage(messageType, payloadNode);
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    await bus.Advanced.Topics.Publish(topic, pubMsg, headers);
                }
                else
                {
                    await bus.Publish(pubMsg, headers);
                }
                break;

            case "send":
                var sendMsg = PrepareMessage(messageType, payloadNode);
                if (!string.IsNullOrWhiteSpace(destination))
                {
                    await bus.Advanced.Routing.Send(destination, sendMsg, headers);
                }
                else
                {
                    await bus.Send(sendMsg, headers);
                }
                break;

            case "sendlocal":
            case "send-local":
                var localMsg = PrepareMessage(messageType, payloadNode);
                await bus.SendLocal(localMsg, headers);
                break;

            case "defer":
                var deferMsg = PrepareMessage(messageType, payloadNode);
                if (!string.IsNullOrWhiteSpace(destination))
                {
                    await bus.Advanced.Routing.Defer(destination, delay, deferMsg, headers);
                }
                else
                {
                    await bus.Defer(delay, deferMsg, headers);
                }
                break;

            case "deferlocal":
            case "defer-local":
                var deferLocalMsg = PrepareMessage(messageType, payloadNode);
                await bus.DeferLocal(delay, deferLocalMsg, headers);
                break;

            default:
                throw new WorkflowTaskException($"rebus: unsupported action '{action}'.");
        }

        if (config["into"]?.GetValue<string>() is { Length: > 0 } into)
        {
            var result = new JsonObject
            {
                ["success"] = true,
                ["action"] = action,
                ["type"] = messageType,
                ["topic"] = topic,
                ["destination"] = destination,
                ["delay"] = delay.ToString()
            };
            JsonPath.Set(context.Data, into, result);
        }
    }

    private static string DetermineAction(TaskDefinition task)
    {
        var configuredAction = task.Config["action"]?.GetValue<string>() ?? task.Config["op"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(configuredAction))
        {
            return configuredAction;
        }

        var taskName = task.Task.ToLowerInvariant();
        return taskName switch
        {
            "rebus-send" or "rebussend" => "send",
            "rebus-send-local" or "rebussendlocal" => "sendLocal",
            "rebus-defer" or "rebusdefer" => "defer",
            "rebus-defer-local" or "rebusdeferlocal" => "deferLocal",
            "rebus-subscribe" or "rebussubscribe" => "subscribe",
            "rebus-unsubscribe" or "rebusunsubscribe" => "unsubscribe",
            _ => "publish"
        };
    }

    private static string DetermineAction(WorkflowTaskContext context)
    {
        var configuredAction = context.RenderText("action") ?? context.RenderText("op");
        if (!string.IsNullOrWhiteSpace(configuredAction))
        {
            return configuredAction;
        }

        var taskName = context.Task.Task.ToLowerInvariant();
        return taskName switch
        {
            "rebus-send" or "rebussend" => "send",
            "rebus-send-local" or "rebussendlocal" => "sendLocal",
            "rebus-defer" or "rebusdefer" => "defer",
            "rebus-defer-local" or "rebusdeferlocal" => "deferLocal",
            "rebus-subscribe" or "rebussubscribe" => "subscribe",
            "rebus-unsubscribe" or "rebusunsubscribe" => "unsubscribe",
            _ => "publish"
        };
    }

    private static TimeSpan ResolveDelay(JsonObject config, WorkflowTaskContext context)
    {
        var rawDelay = context.RenderText("delay");
        if (!string.IsNullOrWhiteSpace(rawDelay))
        {
            return Durations.Parse(rawDelay);
        }

        if (config["deferSeconds"]?.GetValue<double>() is { } sec)
        {
            return TimeSpan.FromSeconds(sec);
        }
        if (config["delaySeconds"]?.GetValue<double>() is { } dsec)
        {
            return TimeSpan.FromSeconds(dsec);
        }
        if (config["delayMinutes"]?.GetValue<double>() is { } dmin)
        {
            return TimeSpan.FromMinutes(dmin);
        }

        return TimeSpan.Zero;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static object PrepareMessage(string? messageType, JsonNode? payloadNode)
    {
        var payloadString = payloadNode is JsonValue jv && jv.TryGetValue<string>(out var s)
            ? s
            : payloadNode?.ToJsonString() ?? "{}";

        if (!string.IsNullOrWhiteSpace(messageType) && ResolveClrType(messageType) is { } clrType)
        {
            try
            {
                var typed = JsonSerializer.Deserialize(payloadString, clrType, JsonOptions);
                if (typed is not null)
                {
                    return typed;
                }
            }
            catch
            {
                // Fall back to RebusWorkflowMessage if deserialization fails
            }
        }

        return new RebusWorkflowMessage(messageType ?? "workflow", payloadString);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> TypeCache = new(StringComparer.OrdinalIgnoreCase);

    private static Type? ResolveClrType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return TypeCache.GetOrAdd(typeName, name =>
        {
            var direct = Type.GetType(name, throwOnError: false, ignoreCase: true);
            if (direct is not null)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var match = assembly.GetType(name, throwOnError: false, ignoreCase: true)
                            ?? assembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        });
    }
}
