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

/// <summary>
/// <c>http</c>: calls an HTTP endpoint.
/// <code>{ "method": "POST", "url": "https://scoring/api/score/{{ data.customerId }}", "headers": {}, "body": { ... },
///   "into": "score", "acceptedStatusCodes": [200], "timeoutSeconds": 30, "client": "my-named-client" }</code>
/// Placeholders are not allowed in the scheme/host part of the URL and are URL-encoded elsewhere (SSRF protection).
/// </summary>
public sealed class HttpTask : IWorkflowTask
{
    public const string DefaultClientName = "middt-workflow";

    public IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["url"]?.GetValue<string>() is not { } url)
        {
            yield return "http: 'url' is required.";
            yield break;
        }

        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        var authorityEnd = schemeEnd < 0 ? -1 : url.IndexOfAny(['/', '?', '#'], schemeEnd + 3);
        var authority = schemeEnd < 0 ? url : url[..(authorityEnd < 0 ? url.Length : authorityEnd)];
        if (schemeEnd < 0 || authority.Contains("{{", StringComparison.Ordinal))
        {
            yield return "http: 'url' must be absolute and must not contain placeholders in the scheme or host.";
        }

        foreach (var error in expressions.ValidateTemplate(task.Config["url"]).Concat(expressions.ValidateTemplate(task.Config["body"])).Concat(expressions.ValidateTemplate(task.Config["headers"])))
        {
            yield return "http: " + error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var method = new HttpMethod(config["method"]?.GetValue<string>()?.ToUpperInvariant() ?? "GET");
        var url = RenderUrl(config["url"]!.GetValue<string>(), context);
        var allowedHosts = context.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MiddtWorkflowOptions>>().Value.AllowedHttpHosts;
        if (allowedHosts.Count > 0 && !allowedHosts.Contains(url.Host))
        {
            throw new WorkflowTaskException($"http: host '{url.Host}' is not in MiddtWorkflowOptions.AllowedHttpHosts.");
        }

        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new WorkflowTaskException($"http: scheme '{url.Scheme}' is not supported.");
        }

        using var request = new HttpRequestMessage(method, url);
        if (context.Render("headers") is JsonObject headers)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value?.ToString());
            }
        }

        if (config["body"] is not null)
        {
            request.Content = new StringContent(context.Render("body")?.ToJsonString() ?? "null", Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        var client = context.Services.GetRequiredService<IHttpClientFactory>().CreateClient(config["client"]?.GetValue<string>() ?? DefaultClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(config["timeoutSeconds"]?.GetValue<int>() ?? 30));

        using var response = await client.SendAsync(request, timeout.Token);
        var accepted = config["acceptedStatusCodes"] is JsonArray codes
            ? codes.Select(c => c!.GetValue<int>()).Contains((int)response.StatusCode)
            : response.IsSuccessStatusCode;

        if (!accepted)
        {
            throw new WorkflowTaskException($"HTTP {method} {url.GetLeftPart(UriPartial.Path)} returned {(int)response.StatusCode}.");
        }

        if (config["into"]?.GetValue<string>() is { Length: > 0 } into)
        {
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            JsonNode? body;
            try
            {
                body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                body = JsonValue.Create(text);
            }

            JsonPath.Set(context.Data, into, new JsonObject { ["statusCode"] = (int)response.StatusCode, ["body"] = body });
        }
    }

    private static Uri RenderUrl(string template, WorkflowTaskContext context)
    {
        // Each placeholder value is escaped: data cannot inject path segments, query parameters or a different host.
        var rendered = System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*(.+?)\s*\}\}", m =>
            Uri.EscapeDataString(Convert.ToString(context.Expressions.Evaluate(m.Groups[1].Value, context.Scope), CultureInfo.InvariantCulture) ?? string.Empty));
        return new Uri(rendered, UriKind.Absolute);
    }
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

    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
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

        return context.SetBusinessKey is { } setKey
            ? setKey(key, cancellationToken)
            : throw new InvalidOperationException("setKey runs inside the workflow engine only.");
    }
}
