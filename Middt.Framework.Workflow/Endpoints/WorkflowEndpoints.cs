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
    /// Maps the workflow REST API under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET /</c> workflows · <c>GET /{workflow}</c> states and transitions</item>
    /// <item><c>POST /{workflow}/instances</c> start · <c>GET /{workflow}/instances</c> list</item>
    /// <item><c>GET /{workflow}/instances/{id}</c> instance with available transitions</item>
    /// <item><c>POST /{workflow}/instances/{id}/transitions/{transition}[?async=true]</c> execute (200, or 202 when queued)</item>
    /// <item><c>GET …/history</c> · <c>GET …/incidents</c> · <c>POST …/retry</c></item>
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
                .Select(d => new WorkflowSummaryModel { Key = d.Key, Version = d.Version, Title = d.Title })
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

            return BaseResponseDataModel<WorkflowDefinitionModel>.Success(ToModel(definition), 1);
        }).WithName("GetWorkflow");

        group.MapPost("{workflow}/instances", async (string workflow, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartWorkflowRequest? request, WorkflowEngine engine, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var (instance, created) = await engine.StartAsync(workflow, request ?? new StartWorkflowRequest(), WorkflowUser.From(principal), ct);
            var body = BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1);
            return created
                ? Results.Created($"{http.Request.Path}/{instance.Id}", body)
                : Results.Ok(body);
        }).WithName("StartWorkflowInstance");

        group.MapGet("{workflow}/instances", async (string workflow, string? state, WorkflowInstanceStatus? status, string? key, int? page, int? pageSize, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var (items, count) = await engine.ListAsync(workflow, WorkflowUser.From(principal), state, status, key, page ?? 1, pageSize ?? 20, ct);
            return BaseResponseDataModel<List<WorkflowInstanceModel>>.Success(items, count);
        }).WithName("ListWorkflowInstances");

        group.MapGet("{workflow}/instances/{id:guid}", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
            BaseResponseDataModel<WorkflowInstanceModel>.Success(EnsureWorkflow(await engine.GetAsync(id, WorkflowUser.From(principal), ct), workflow), 1))
            .WithName("GetWorkflowInstance");

        group.MapPost("{workflow}/instances/{id:guid}/transitions/{transition}", async (string workflow, Guid id, string transition, bool? async, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonObject? input, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = WorkflowUser.From(principal);
            EnsureWorkflow(await engine.GetAsync(id, user, ct), workflow);
            var instance = await engine.ExecuteTransitionAsync(id, transition, input, user, async == true, ct);
            var body = BaseResponseDataModel<WorkflowInstanceModel>.Success(instance, 1);
            return async == true ? Results.Accepted(null, body) : Results.Ok(body);
        }).WithName("ExecuteWorkflowTransition");

        group.MapGet("{workflow}/instances/{id:guid}/history", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = WorkflowUser.From(principal);
            EnsureWorkflow(await engine.GetAsync(id, user, ct), workflow);
            var items = await engine.HistoryAsync(id, user, ct);
            return BaseResponseDataModel<List<WorkflowHistoryModel>>.Success(items, items.Count);
        }).WithName("GetWorkflowInstanceHistory");

        group.MapGet("{workflow}/instances/{id:guid}/incidents", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = WorkflowUser.From(principal);
            EnsureWorkflow(await engine.GetAsync(id, user, ct), workflow);
            var items = await engine.IncidentsAsync(id, user, ct);
            return BaseResponseDataModel<List<WorkflowIncidentModel>>.Success(items, items.Count);
        }).WithName("GetWorkflowInstanceIncidents");

        group.MapPost("{workflow}/instances/{id:guid}/retry", async (string workflow, Guid id, WorkflowEngine engine, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = WorkflowUser.From(principal);
            EnsureWorkflow(await engine.GetAsync(id, user, ct), workflow);
            return BaseResponseDataModel<WorkflowInstanceModel>.Success(await engine.RetryAsync(id, user, ct), 1);
        }).WithName("RetryWorkflowInstance");

        return group;
    }

    private static WorkflowInstanceModel EnsureWorkflow(WorkflowInstanceModel instance, string workflow) =>
        instance.Workflow == workflow ? instance : throw new WorkflowNotFoundException($"Instance '{instance.Id}' does not belong to workflow '{workflow}'.");

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

    private static WorkflowDefinitionModel ToModel(WorkflowDefinition definition) => new()
    {
        Key = definition.Key,
        Version = definition.Version,
        Title = definition.Title,
        StartState = definition.Start.Target,
        StartInputSchema = (JsonObject?)definition.Start.Input?.Schema?.DeepClone(),
        States = definition.States.Select(s => new WorkflowStateModel
        {
            Key = s.Key,
            Title = s.Title,
            Type = s.Type.ToString(),
            SubType = s.SubType.ToString(),
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
