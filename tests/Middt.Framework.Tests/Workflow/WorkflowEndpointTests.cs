using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Model.Workflow;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowEndpointTests
{
    private const string Base = "api/workflows";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<WorkflowTestHost> Host() =>
        WorkflowTestHost.StartAsync(TestWorkflows.Pipeline, TestWorkflows.Restricted, TestWorkflows.Errors, TestWorkflows.Timers);

    private static async Task<HttpResponseMessage> SendAsync(WorkflowTestHost host, HttpMethod method, string uri, object? body = null, string? user = "alice", params string[] roles)
    {
        using var request = host.Request(method, uri, body, user, roles);
        return await host.Client.SendAsync(request, Ct);
    }

    private static async Task<BaseResponseDataModel<T>> EnvelopeAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<T>>(WorkflowTestHost.Json, Ct))!;

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(WorkflowTestHost.Json, Ct))!;
    }

    private static async Task<WorkflowInstanceModel> StartAsync(WorkflowTestHost host, string workflow = "pipeline", object? body = null)
    {
        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/{workflow}/instances", body ?? new { data = new { name = "widget" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await EnvelopeAsync<WorkflowInstanceModel>(response)).Data!;
    }

    [Fact]
    public async Task Requests_without_authentication_are_rejected()
    {
        await using var host = await Host();

        using var response = await SendAsync(host, HttpMethod.Get, Base, user: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Workflow_list_is_filtered_by_workflow_roles()
    {
        await using var host = await Host();

        using var user = await SendAsync(host, HttpMethod.Get, Base);
        using var clerk = await SendAsync(host, HttpMethod.Get, Base, roles: "clerk");

        var visible = await EnvelopeAsync<List<WorkflowSummaryModel>>(user);
        visible.Result.Should().Be(ResultEnum.Success);
        visible.Data!.Select(w => w.Key).Should().Equal("errors", "pipeline", "timers");
        visible.Count.Should().Be(3);
        (await EnvelopeAsync<List<WorkflowSummaryModel>>(clerk)).Data!.Select(w => w.Key).Should().Contain("restricted");
    }

    [Fact]
    public async Task Workflow_definition_endpoint_describes_states_and_transitions()
    {
        await using var host = await Host();

        using var response = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline");
        using var forbidden = await SendAsync(host, HttpMethod.Get, $"{Base}/restricted");
        using var missing = await SendAsync(host, HttpMethod.Get, $"{Base}/nope");
        using var missingVersion = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline?version=9");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var model = (await EnvelopeAsync<WorkflowDefinitionModel>(response)).Data!;
        model.Title.Should().Be("Pipeline");
        model.StartState.Should().Be("draft");
        model.StartInputSchema.Should().NotBeNull();
        model.States.Select(s => s.Key).Should().Equal("draft", "review", "approved", "cancelled");
        model.Transitions.Should().Contain(t => t.Key == "submit" && t.From == "draft" && t.Target == "review" && t.Trigger == "Manual");
        model.Transitions.Should().Contain(t => t.Key == "cancel" && t.From == null);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingVersion.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_returns_201_with_location_and_envelope()
    {
        await using var host = await Host();

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances", new { data = new { name = "widget" } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await EnvelopeAsync<WorkflowInstanceModel>(response);
        envelope.Result.Should().Be(ResultEnum.Success);
        envelope.Count.Should().Be(1);
        envelope.Data!.State.Should().Be("draft");
        envelope.Data.Status.Should().Be(WorkflowInstanceStatus.Active);
        envelope.Data.StartedBy.Should().Be("alice");
        response.Headers.Location!.ToString().Should().Be($"/{Base}/pipeline/instances/{envelope.Data.Id}");

        using var get = await SendAsync(host, HttpMethod.Get, response.Headers.Location.ToString());
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Start_without_a_body_is_allowed_for_workflows_without_input()
    {
        await using var host = await Host();

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/restricted/instances", roles: "clerk");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Start_with_an_existing_key_returns_200_and_the_same_instance()
    {
        await using var host = await Host();
        var body = new { key = "order-9", data = new { name = "widget" } };

        using var first = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances", body);
        using var second = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances", body);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Location.Should().BeNull();
        (await EnvelopeAsync<WorkflowInstanceModel>(second)).Data!.Id.Should().Be((await EnvelopeAsync<WorkflowInstanceModel>(first)).Data!.Id);
    }

    [Fact]
    public async Task Invalid_input_returns_a_validation_problem()
    {
        await using var host = await Host();

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances", new { data = new { name = "x", role = "admin" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(WorkflowTestHost.Json, Ct))!;
        problem.Title.Should().Be("The request is not valid for this transition.");
        problem.Errors["input"].Should().BeEquivalentTo("$.name: must be at least 2 characters.", "$.role: is not allowed.");
    }

    [Fact]
    public async Task Get_returns_the_instance_with_transitions_available_to_the_caller()
    {
        await using var host = await Host();
        var instance = await StartAsync(host);

        using var asUser = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline/instances/{instance.Id}");
        using var asManager = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline/instances/{instance.Id}", roles: "manager");

        asUser.StatusCode.Should().Be(HttpStatusCode.OK);
        var model = (await EnvelopeAsync<WorkflowInstanceModel>(asUser)).Data!;
        model.Data["name"]!.GetValue<string>().Should().Be("widget");
        model.AvailableTransitions.Select(t => t.Key).Should().BeEquivalentTo("submit", "plain", "touch", "cancel");
        model.AvailableTransitions.Single(t => t.Key == "submit").InputSchema.Should().NotBeNull();
        (await EnvelopeAsync<WorkflowInstanceModel>(asManager)).Data!.AvailableTransitions.Select(t => t.Key).Should().Contain("approve");
    }

    [Fact]
    public async Task Instance_requested_under_another_workflow_or_unknown_id_is_404()
    {
        await using var host = await Host();
        var instance = await StartAsync(host);

        using var wrongWorkflow = await SendAsync(host, HttpMethod.Get, $"{Base}/timers/instances/{instance.Id}");
        using var wrongWorkflowTransition = await SendAsync(host, HttpMethod.Post, $"{Base}/timers/instances/{instance.Id}/transitions/submit");
        using var unknown = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline/instances/{Guid.NewGuid()}");

        wrongWorkflow.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemAsync(wrongWorkflow)).Title.Should().Contain("does not belong to workflow 'timers'");
        wrongWorkflowTransition.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.GetAsync(instance.Id)).State.Should().Be("draft");
    }

    [Fact]
    public async Task Transition_returns_200_with_the_updated_instance()
    {
        await using var host = await Host();
        var instance = await StartAsync(host);

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{instance.Id}/transitions/submit", new { comment = "go" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var model = (await EnvelopeAsync<WorkflowInstanceModel>(response)).Data!;
        model.State.Should().Be("review");
        model.Data["submission"]!["comment"]!.GetValue<string>().Should().Be("go");
    }

    [Fact]
    public async Task Transition_without_the_required_role_is_403()
    {
        await using var host = await Host();
        var instance = await StartAsync(host);

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{instance.Id}/transitions/approve");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemAsync(response)).Title.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Workflow_roles_are_enforced_on_instances()
    {
        await using var host = await Host();
        using var start = await SendAsync(host, HttpMethod.Post, $"{Base}/restricted/instances", roles: "clerk");
        var id = (await EnvelopeAsync<WorkflowInstanceModel>(start)).Data!.Id;

        using var startForbidden = await SendAsync(host, HttpMethod.Post, $"{Base}/restricted/instances");
        using var getForbidden = await SendAsync(host, HttpMethod.Get, $"{Base}/restricted/instances/{id}");
        using var listForbidden = await SendAsync(host, HttpMethod.Get, $"{Base}/restricted/instances");
        using var historyForbidden = await SendAsync(host, HttpMethod.Get, $"{Base}/restricted/instances/{id}/history");

        startForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        getForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        listForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        historyForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unavailable_transition_terminal_and_busy_instances_are_409()
    {
        await using var host = await Host();
        var instance = await StartAsync(host);
        var busy = await StartAsync(host);
        await host.SetStatusAsync(busy.Id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        using var unavailable = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{instance.Id}/transitions/nope");
        using var busyResponse = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{busy.Id}/transitions/submit");
        using var cancel = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{instance.Id}/transitions/cancel");
        using var afterFinish = await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{instance.Id}/transitions/submit");

        unavailable.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemAsync(unavailable)).Title.Should().Be("Transition 'nope' is not available in state 'draft'.");
        busyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemAsync(busyResponse)).Title.Should().Contain("busy");
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        afterFinish.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Async_transition_returns_202_and_is_executed_by_the_processor()
    {
        await using var host = await Host();
        var instance = await StartAsync(host, "timers", new { });

        using var response = await SendAsync(host, HttpMethod.Post, $"{Base}/timers/instances/{instance.Id}/transitions/proceed?async=true");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EnvelopeAsync<WorkflowInstanceModel>(response)).Data!.State.Should().Be("waiting");

        await host.RunJobsAsync();
        (await host.GetAsync(instance.Id)).State.Should().Be("working");
    }

    [Fact]
    public async Task History_incidents_and_retry_endpoints()
    {
        await using var host = await Host();
        var instance = await StartAsync(host, "errors", new { });
        var uri = $"{Base}/errors/instances/{instance.Id}";

        using var notFaulted = await SendAsync(host, HttpMethod.Post, $"{uri}/retry");
        notFaulted.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var fault = await SendAsync(host, HttpMethod.Post, $"{uri}/transitions/fault", new { attempt = 3 });
        fault.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeAsync<WorkflowInstanceModel>(fault)).Data!.Status.Should().Be(WorkflowInstanceStatus.Faulted);

        using var incidents = await SendAsync(host, HttpMethod.Get, $"{uri}/incidents");
        var incidentList = await EnvelopeAsync<List<WorkflowIncidentModel>>(incidents);
        incidentList.Count.Should().Be(1);
        incidentList.Data!.Single().Should().Match<WorkflowIncidentModel>(i => i.TaskKey == "fault-once" && i.ResolvedAt == null);

        using var retry = await SendAsync(host, HttpMethod.Post, $"{uri}/retry");
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var retried = (await EnvelopeAsync<WorkflowInstanceModel>(retry)).Data!;
        retried.Status.Should().Be(WorkflowInstanceStatus.Completed);
        retried.Data["faultInput"]!["attempt"]!.GetValue<int>().Should().Be(3);

        using var resolved = await SendAsync(host, HttpMethod.Get, $"{uri}/incidents");
        (await EnvelopeAsync<List<WorkflowIncidentModel>>(resolved)).Data!.Single().ResolvedAt.Should().NotBeNull();

        using var history = await SendAsync(host, HttpMethod.Get, $"{uri}/history");
        var items = await EnvelopeAsync<List<WorkflowHistoryModel>>(history);
        items.Data!.Select(h => h.TransitionKey).Should().Equal("start", "fault");
        items.Data!.Select(h => h.Trigger).Should().Equal("Start", "Retry");
        items.Count.Should().Be(2);
    }

    [Fact]
    public async Task List_filters_by_state_status_and_key_and_reports_the_total_count()
    {
        await using var host = await Host();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await StartAsync(host, "pipeline", new { key = $"k{i}", data = new { name = $"item{i}" } })).Id);
            host.Time.Advance(TimeSpan.FromSeconds(1));
        }

        (await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{ids[0]}/transitions/cancel")).Dispose();
        (await SendAsync(host, HttpMethod.Post, $"{Base}/pipeline/instances/{ids[1]}/transitions/submit")).Dispose();

        async Task<BaseResponseDataModel<List<WorkflowInstanceModel>>> ListAsync(string query)
        {
            using var response = await SendAsync(host, HttpMethod.Get, $"{Base}/pipeline/instances{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await EnvelopeAsync<List<WorkflowInstanceModel>>(response);
        }

        (await ListAsync("")).Count.Should().Be(5);
        (await ListAsync("?state=draft")).Data!.Select(i => i.Key).Should().Equal("k4", "k3", "k2");
        (await ListAsync("?status=Cancelled")).Data!.Should().ContainSingle().Which.Id.Should().Be(ids[0]);
        (await ListAsync("?key=k1")).Data!.Should().ContainSingle().Which.State.Should().Be("review");

        var page = await ListAsync("?page=2&pageSize=2");
        page.Count.Should().Be(5);
        page.Data!.Select(i => i.Key).Should().Equal("k2", "k1");
        page.Data!.Should().AllSatisfy(i => i.AvailableTransitions.Should().NotBeEmpty());
    }
}
