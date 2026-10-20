using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Workflow.Endpoints;

public static class WorkflowEndpoints
{
    /// <summary>
    /// <c>messageList</c> entry of a start whose business key already belonged to an instance (200 instead of 201): that
    /// instance is returned and nothing new was started. Clients show it instead of "started".
    /// </summary>
    public const string ExistingInstanceMessage = "Bu iş anahtarıyla açık bir akış zaten var; mevcut akış açıldı.";

    /// <summary>
    /// Maps the workflow REST API under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET /</c> workflows · <c>GET /{workflow}</c> states and transitions</item>
    /// <item><c>POST /{workflow}/instances</c> start · <c>GET /{workflow}/instances</c> list</item>
    /// <item><c>GET /{workflow}/instances/{id}</c> instance with available transitions</item>
    /// <item><c>POST /{workflow}/instances/{id}/transitions/{transition}[?async=true]</c> execute (200, or 202 when queued)</item>
    /// <item><c>GET …/history</c> · <c>GET …/incidents</c> · <c>POST …/retry</c></item>
    /// <item><c>POST /{workflow}/instances/{id}/messages/{message}</c> · <c>POST /{workflow}/messages/{message}?key=</c>
    /// deliver a message to an event transition (200, or 202 when buffered with <c>?bufferFor=PT10M</c>)</item>
    /// </list>
    /// Responses use <see cref="BaseResponseDataModel{T}"/>; errors are ProblemDetails (400/403/404/409).
    /// </summary>
    public static RouteGroupBuilder MapMiddtWorkflows(this IEndpointRouteBuilder endpoints, string prefix = "api/workflows", bool requireAuthorization = true)
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Workflows")
            .AddEndpointFilter(HandleWorkflowErrors);

        if (requireAuthorization)
        {
            group.RequireAuthorization();
        }

        group.MapGet("", (WorkflowRegistry registry, ClaimsPrincipal principal) =>
        {
            var user = WorkflowUser.From(principal);
            var items = registry.Latest
                .Where(d => d.Roles.Count == 0 || d.Roles.Any(user.IsInRole))
                .Select(d => new WorkflowSummaryModel { Key = d.Key, Version = d.Version, Title = d.Title, Icon = d.Icon })
                .OrderBy(d => d.Key, StringComparer.Ordinal)
                .ToList();
            return BaseResponseDataModel<List<WorkflowSummaryModel>>.Success(items, items.Count);
        }).WithName("ListWorkflows");

        group.MapGet("{workflow}", (string workflow, int? version, WorkflowRegistry registry, ClaimsPrincipal principal) =>
        {
            var definition = version is { } v ? registry.Get(workflow, v) : registry.GetLatest(workflow);
            var user = WorkflowUser.From(principal);
            if (definition.Roles.Count > 0 && !definition.Roles.Any(user.IsInRole))
            {
                throw new WorkflowForbiddenException($"You are not allowed to access workflow '{workflow}'.");
            }

            return BaseResponseDataModel<WorkflowDefinitionModel>.Success(ToModel(definition, user), 1);
        }).WithName("GetWorkflow");

        group.MapPost("{workflow}/instances", async (string workflow, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartWorkflowRequest? request, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var startRequest = request ?? new StartWorkflowRequest();
            startRequest.Data = WorkflowHttpInput.FromHttpContext(http, startRequest.Data);
            var (instance, created) = await engine.StartAsync(workflow, startRequest, WorkflowUser.From(principal), ct);
            http.Response.Headers.ETag = ComputeInstanceETag(instance);
            var body = BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1);
            if (created)
            {
                return Results.Created($"{http.Request.Path}/{instance.Id}", body);
            }

            // Same business key: the existing instance is returned (200). The message tells clients that nothing was started.
            body.MessageList.Add(ExistingInstanceMessage);
            return Results.Ok(body);
        }).WithName("StartWorkflowInstance");

        group.MapGet("{workflow}/instances", async (string workflow, string? state, WorkflowInstanceStatus? status, string? key, int? page, int? pageSize, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var filter = ExtractDataFilter(http.Request.Query);
            var (items, count) = await engine.ListAsync(workflow, WorkflowUser.From(principal), state, status, key, page ?? 1, pageSize ?? 20, filter, ct);
            return BaseResponseDataModel<List<WorkflowInstanceModel>>.Success(items, count);
        }).WithName("ListWorkflowInstances");

        group.MapGet("{workflow}/instances/{id:guid}", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var instance = await engine.GetAsync(workflow, id, WorkflowUser.From(principal), ct);
            var etag = ComputeInstanceETag(instance);
            http.Response.Headers.ETag = etag;
            if (http.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',', StringSplitOptions.TrimEntries).Any(t => t == "*" || string.Equals(t, etag, StringComparison.Ordinal))))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Ok(BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1));
        }).WithName("GetWorkflowInstance");

        group.MapPost("{workflow}/instances/{id:guid}/transitions/{transition}", async (string workflow, Guid id, string transition, bool? async, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonObject? input, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            // The engine checks access and the workflow on the row it loads anyway (no separate read).
            var enrichedInput = WorkflowHttpInput.FromHttpContext(http, input);
            var instance = await engine.ExecuteTransitionAsync(workflow, id, transition, enrichedInput, WorkflowUser.From(principal), async == true, ct);
            http.Response.Headers.ETag = ComputeInstanceETag(instance);
            var body = BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1);
            return async == true ? Results.Accepted(null, body) : Results.Ok(body);
        }).WithName("ExecuteWorkflowTransition");

        group.MapGet("{workflow}/instances/{id:guid}/view", async (string workflow, Guid id, WorkflowEngine engine, WorkflowViewRenderer renderer, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var instance = await engine.GetAsync(workflow, id, WorkflowUser.From(principal), ct);
            var html = await renderer.RenderAsync(instance, WorkflowUser.From(principal), ct)
                ?? throw new WorkflowNotFoundException($"State '{instance.State}' has no registered view.");
            return Results.Content(html, "text/html; charset=utf-8");
        }).WithName("RenderWorkflowInstanceView");

        group.MapGet("{workflow}/instances/{id:guid}/history", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var items = await engine.HistoryAsync(workflow, id, WorkflowUser.From(principal), ct);
            return BaseResponseDataModel<List<WorkflowHistoryModel>>.Success(items, items.Count);
        }).WithName("GetWorkflowInstanceHistory");

        group.MapGet("{workflow}/instances/{id:guid}/incidents", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var items = await engine.IncidentsAsync(workflow, id, WorkflowUser.From(principal), ct);
            return BaseResponseDataModel<List<WorkflowIncidentModel>>.Success(items, items.Count);
        }).WithName("GetWorkflowInstanceIncidents");

        group.MapPost("{workflow}/instances/{id:guid}/retry", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            return BaseResponseDataModel<WorkflowInstanceModel>.Success(await engine.RetryAsync(workflow, id, WorkflowUser.From(principal), ct), 1);
        }).WithName("RetryWorkflowInstance");

        group.MapPost("{workflow}/instances/{id:guid}/messages/{message}", (string workflow, Guid id, string message, string? bufferFor, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonObject? data, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
            DeliverMessageAsync(new WorkflowMessage { Name = message, Workflow = workflow, InstanceId = id, Data = data, BufferFor = ParseBuffer(bufferFor) }, engine, principal, http, ct))
            .WithName("DeliverWorkflowInstanceMessage");

        group.MapPost("{workflow}/messages/{message}", (string workflow, string message, string? key, string? bufferFor, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonObject? data, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
            DeliverMessageAsync(new WorkflowMessage { Name = message, Workflow = workflow, Key = key, Data = data, BufferFor = ParseBuffer(bufferFor) }, engine, principal, http, ct))
            .WithName("DeliverWorkflowMessage");

        return group;
    }

    private static async Task<IResult> DeliverMessageAsync(WorkflowMessage message, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct)
    {
        var user = WorkflowUser.From(principal);
        var result = await engine.DeliverMessageAsync(message, user, ct);
        var instance = result.Instance ?? await engine.GetAsync(message.Workflow, result.InstanceId, user, ct);
        http.Response.Headers.ETag = ComputeInstanceETag(instance);
        var body = BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1);
        return result.Outcome == WorkflowMessageOutcome.Buffered ? Results.Accepted(null, body) : Results.Ok(body);
    }

    private static TimeSpan ParseBuffer(string? bufferFor) =>
        string.IsNullOrWhiteSpace(bufferFor) ? TimeSpan.Zero
        : Middt.Framework.Expressions.Durations.TryParse(bufferFor, out var duration) ? duration
        : throw new WorkflowValidationException([$"bufferFor '{bufferFor}' is not a valid duration (ISO-8601 such as PT10M, or hh:mm:ss)."]);

    private static async ValueTask<object?> HandleWorkflowErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (WorkflowValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["input"] = [.. ex.Errors] }, title: ex.Message);
        }
        catch (WorkflowException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>Projects a <see cref="WorkflowDefinition"/> to its client <see cref="WorkflowDefinitionModel"/> contract.</summary>
    public static WorkflowDefinitionModel ToModel(WorkflowDefinition definition, Middt.Framework.Expressions.ExpressionUser? user = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new WorkflowDefinitionModel
        {
            Key = definition.Key,
            Version = definition.Version,
            Title = definition.Title,
            Icon = definition.Icon,
            StartState = definition.Start.Target,
            StartInputSchema = (JsonObject?)definition.Start.Input?.Schema?.DeepClone(),
            DataSchema = Schema.JsonSchemaValidator.ProjectSchema(definition.DataSchema, user ?? Expressions.WorkflowUser.System),
            States = definition.States.Select(s => new WorkflowStateModel
            {
                Key = s.Key,
                Title = s.Title,
                Type = s.Type.ToString(),
                SubType = s.SubType.ToString(),
                View = s.View?.Component,
                Layout = s.View?.Layout,
                Step = s.Step,
            }).ToList(),
            Transitions = definition.States
                .SelectMany(s => s.Transitions.Select(t => (From: (string?)s.Key, Transition: t)))
                .Concat(definition.SharedTransitions.Select(t => (From: (string?)null, Transition: t)))
                .Select(x => new WorkflowTransitionModel
                {
                    Key = x.Transition.Key,
                    Title = x.Transition.Title,
                    From = x.From,
                    Target = x.Transition.Target,
                    Trigger = x.Transition.Trigger.ToString(),
                })
                .ToList(),
        };
    }

    private static readonly HashSet<string> ReservedInstanceQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "state", "status", "key", "page", "pageSize",
    };

    private static Dictionary<string, string?>? ExtractDataFilter(IQueryCollection query)
    {
        if (query.Count == 0)
        {
            return null;
        }

        Dictionary<string, string?>? result = null;
        foreach (var (rawKey, values) in query)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || ReservedInstanceQueryKeys.Contains(rawKey))
            {
                continue;
            }

            var value = values.FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var fieldKey = rawKey.StartsWith("data.", StringComparison.OrdinalIgnoreCase)
                ? rawKey[5..]
                : rawKey.StartsWith("filter.", StringComparison.OrdinalIgnoreCase)
                    ? rawKey[7..]
                    : rawKey;

            if (!string.IsNullOrWhiteSpace(fieldKey))
            {
                result ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                result[fieldKey] = value;
            }
        }

        return result;
    }

    private static string ComputeInstanceETag(WorkflowInstanceModel instance) =>
        $"\"{instance.Id:N}-v{instance.DataVersion}-{instance.State}-{instance.Status}-{instance.UpdatedAt.Ticks}-{instance.ActiveSubFlow?.DataVersion}-{instance.ActiveSubFlow?.State}\"";
}
