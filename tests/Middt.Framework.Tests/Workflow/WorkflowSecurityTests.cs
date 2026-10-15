using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

/// <summary>Owner-only instance access, hidden error texts and the SSRF guards of the <c>http</c> task.</summary>
public sealed class WorkflowSecurityTests
{
    private const string Base = "api/workflows";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string OwnedWorkflow = """
        {
          "key": "owned",
          "instanceAccess": { "mode": "owner", "supervisorRoles": [ "manager" ] },
          "start": { "key": "start", "target": "open" },
          "states": [
            { "key": "open", "transitions": [
                { "key": "touch", "target": "$self" },
                { "key": "break", "target": "closed", "onExecute": [ { "task": "fail", "config": { "message": "db password=hunter2 at 10.0.0.5" } } ] },
                { "key": "close", "target": "closed" } ] },
            { "key": "closed", "type": "Finish" }
          ]
        }
        """;

    private static async Task<HttpResponseMessage> SendAsync(WorkflowTestHost host, HttpMethod method, string uri, string user, params string[] roles)
    {
        using var request = host.Request(method, uri, null, user, roles);
        return await host.Client.SendAsync(request, Ct);
    }

    private static async Task<BaseResponseDataModel<T>> EnvelopeAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<T>>(WorkflowTestHost.Json, Ct))!;

    private static async Task<(WorkflowTestHost Host, Guid AliceInstance, Guid BobInstance)> OwnedHostAsync()
    {
        var host = await WorkflowTestHost.StartAsync([OwnedWorkflow]);
        var alice = await host.StartInstanceAsync("owned", user: WorkflowTestHost.User("alice"));
        var bob = await host.StartInstanceAsync("owned", user: WorkflowTestHost.User("bob"));
        return (host, alice.Id, bob.Id);
    }

    // ───────────────────────────── instanceAccess ─────────────────────────────

    [Fact]
    public async Task Owner_mode_lists_only_the_callers_own_instances()
    {
        var (host, alice, bob) = await OwnedHostAsync();
        await using var _ = host;

        using var response = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances", "alice");

        var list = await EnvelopeAsync<List<WorkflowInstanceModel>>(response);
        list.Data!.Select(i => i.Id).Should().Equal(alice);
        list.Count.Should().Be(1, "the total count must not reveal other users' instances");
        list.Data!.Should().NotContain(i => i.Id == bob);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/history")]
    [InlineData("GET", "/incidents")]
    [InlineData("POST", "/transitions/close")]
    [InlineData("POST", "/retry")]
    public async Task Owner_mode_answers_404_for_instances_of_other_users(string method, string suffix)
    {
        var (host, _, bob) = await OwnedHostAsync();
        await using var __ = host;

        using var response = await SendAsync(host, new HttpMethod(method), $"{Base}/owned/instances/{bob}{suffix}", "alice");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var row = await host.RowAsync(bob);
        row.State.Should().Be("open", "a foreign instance must not be changed");
        row.Status.Should().Be(WorkflowInstanceStatus.Active);
    }

    [Fact]
    public async Task Owner_mode_lets_the_owner_drive_and_read_the_instance()
    {
        var (host, alice, _) = await OwnedHostAsync();
        await using var _ = host;

        using var get = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances/{alice}", "alice");
        using var history = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances/{alice}/history", "alice");
        using var close = await SendAsync(host, HttpMethod.Post, $"{Base}/owned/instances/{alice}/transitions/close", "alice");

        get.StatusCode.Should().Be(HttpStatusCode.OK);
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeAsync<WorkflowInstanceModel>(close)).Data!.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Supervisor_role_sees_and_drives_every_instance()
    {
        var (host, alice, bob) = await OwnedHostAsync();
        await using var _ = host;

        using var list = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances", "carol", "manager");
        using var get = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances/{bob}", "carol", "manager");
        using var close = await SendAsync(host, HttpMethod.Post, $"{Base}/owned/instances/{alice}/transitions/close", "carol", "manager");

        (await EnvelopeAsync<List<WorkflowInstanceModel>>(list)).Data!.Select(i => i.Id).Should().BeEquivalentTo([alice, bob]);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.RowAsync(alice)).Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Owner_mode_engine_api_hides_foreign_instances()
    {
        var (host, _, bob) = await OwnedHostAsync();
        await using var __ = host;
        var alice = WorkflowTestHost.User("alice");

        await FluentActions.Awaiting(() => host.WithEngineAsync(e => e.GetAsync(bob, alice, Ct))).Should().ThrowAsync<WorkflowNotFoundException>();
        await FluentActions.Awaiting(() => host.WithEngineAsync(e => e.HistoryAsync(bob, alice, Ct))).Should().ThrowAsync<WorkflowNotFoundException>();
        await FluentActions.Awaiting(() => host.WithEngineAsync(e => e.IncidentsAsync(bob, alice, Ct))).Should().ThrowAsync<WorkflowNotFoundException>();
        await FluentActions.Awaiting(() => host.TransitionAsync(bob, "close", user: alice)).Should().ThrowAsync<WorkflowNotFoundException>();
        await FluentActions.Awaiting(() => host.RetryAsync(bob, alice)).Should().ThrowAsync<WorkflowNotFoundException>();
        (await host.WithEngineAsync(e => e.ListAsync("owned", alice, null, null, null, 1, 20, Ct))).Count.Should().Be(1);
    }

    [Fact]
    public async Task Owner_mode_start_with_a_foreign_business_key_does_not_disclose_the_instance()
    {
        await using var host = await WorkflowTestHost.StartAsync([OwnedWorkflow]);
        var bob = await host.StartInstanceAsync("owned", key: "record-1", user: WorkflowTestHost.User("bob"));

        await FluentActions.Awaiting(() => host.StartWithFlagAsync("owned", key: "record-1", user: WorkflowTestHost.User("alice")))
            .Should().ThrowAsync<WorkflowNotFoundException>();
        var (asBob, created) = await host.StartWithFlagAsync("owned", key: "record-1", user: WorkflowTestHost.User("bob"));
        var (asManager, _) = await host.StartWithFlagAsync("owned", key: "record-1", user: WorkflowTestHost.User("boss", "manager"));

        created.Should().BeFalse();
        asBob.Id.Should().Be(bob.Id);
        asManager.Id.Should().Be(bob.Id, "supervisors see every instance");
    }

    [Fact]
    public async Task Instances_of_another_workflow_answer_404_on_every_instance_endpoint()
    {
        await using var host = await WorkflowTestHost.StartAsync([OwnedWorkflow, TestWorkflows.Pipeline]);
        var pipeline = await host.StartInstanceAsync("pipeline", new { name = "widget" }, user: WorkflowTestHost.User("alice"));

        foreach (var (method, suffix) in new[] { ("GET", ""), ("GET", "/history"), ("GET", "/incidents"), ("POST", "/transitions/plain"), ("POST", "/retry") })
        {
            using var response = await SendAsync(host, new HttpMethod(method), $"{Base}/owned/instances/{pipeline.Id}{suffix}", "alice");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {suffix}");
        }

        (await host.RowAsync(pipeline.Id)).State.Should().Be("draft");
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("owners")]
    [InlineData("")]
    public void Invalid_instanceAccess_mode_is_rejected_at_startup(string mode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            o.AddDefinition(OwnedWorkflow.Replace("\"mode\": \"owner\"", $"\"mode\": \"{mode}\"", StringComparison.Ordinal), "owned.json");
            o.AddTask<FailTask>("fail");
        });
        services.AddSingleton<FlakyCounter>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<WorkflowRegistry>().EnsureLoaded();

        act.Should().Throw<WorkflowDefinitionException>().Which.Errors.Should().Contain(e => e.Contains("instanceAccess.mode", StringComparison.Ordinal));
    }

    // ───────────────────────────── ExposeErrorDetails ─────────────────────────────

    [Fact]
    public async Task Error_details_are_hidden_from_callers_by_default()
    {
        await using var host = await WorkflowTestHost.StartAsync([OwnedWorkflow], configure: o => o.ExposeErrorDetails = false);
        var instance = await host.StartInstanceAsync("owned");

        var faulted = await host.TransitionAsync(instance.Id, "break");

        faulted.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        faulted.Error.Should().Be(WorkflowEngine.HiddenErrorText);
        (await host.GetAsync(instance.Id)).Error.Should().Be(WorkflowEngine.HiddenErrorText);
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle().Which.Error.Should().Be(WorkflowEngine.HiddenErrorText);

        using var response = await SendAsync(host, HttpMethod.Get, $"{Base}/owned/instances/{instance.Id}/incidents", "alice");
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("hunter2").And.NotContain("10.0.0.5");

        // Operators still find the details in the database.
        (await host.RowAsync(instance.Id)).Error.Should().Contain("hunter2");
    }

    [Fact]
    public async Task Error_details_are_returned_when_enabled()
    {
        await using var host = await WorkflowTestHost.StartAsync([OwnedWorkflow], configure: o => o.ExposeErrorDetails = true);
        var instance = await host.StartInstanceAsync("owned");

        var faulted = await host.TransitionAsync(instance.Id, "break");

        faulted.Error.Should().Contain("hunter2");
        (await host.IncidentsAsync(instance.Id)).Single().Error.Should().Contain("hunter2");
    }

    [Fact]
    public async Task Instances_without_errors_report_no_error_text_when_hidden()
    {
        await using var host = await WorkflowTestHost.StartAsync([OwnedWorkflow], configure: o => o.ExposeErrorDetails = false);

        var instance = await host.StartInstanceAsync("owned");

        instance.Error.Should().BeNull();
    }

    // ───────────────────────────── http task ─────────────────────────────

    private static string HttpDefinition(string url) => $$"""
        {
          "key": "http-guard",
          "start": { "key": "start", "target": "calling" },
          "states": [ { "key": "calling", "onEntry": [ { "task": "http", "key": "call", "config": { "url": "{{url}}", "into": "result" } } ] } ]
        }
        """;

    [Fact]
    public async Task Http_task_refuses_hosts_outside_AllowedHttpHosts()
    {
        var handler = new FakeHttpHandler();
        await using var host = await WorkflowTestHost.StartAsync(
            [HttpDefinition("https://internal.test/admin")],
            configure: o => o.AllowedHttpHosts.Add("scoring.test"),
            services: s => s.AddHttpClient(Middt.Framework.Workflow.Tasks.HttpTask.DefaultClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

        var instance = await host.StartInstanceAsync("http-guard");

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        handler.Requests.Should().BeEmpty("the request must not be sent");
        (await host.IncidentsAsync(instance.Id)).Single().Error.Should().Contain("AllowedHttpHosts");
    }

    [Fact]
    public async Task Http_task_calls_hosts_listed_in_AllowedHttpHosts_case_insensitively()
    {
        var handler = new FakeHttpHandler();
        await using var host = await WorkflowTestHost.StartAsync(
            [HttpDefinition("https://Scoring.Test/score")],
            configure: o => o.AllowedHttpHosts.Add("scoring.test"),
            services: s => s.AddHttpClient(Middt.Framework.Workflow.Tasks.HttpTask.DefaultClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

        var instance = await host.StartInstanceAsync("http-guard");

        instance.Status.Should().Be(WorkflowInstanceStatus.Active);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Http_task_does_not_follow_redirects()
    {
        // A real Kestrel endpoint and the framework's own HttpClient registration (no test handler).
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var target = builder.Build();
        var internalHits = 0;
        target.MapGet("/start", () => Results.Redirect("/internal-secret"));
        target.MapGet("/internal-secret", () =>
        {
            Interlocked.Increment(ref internalHits);
            return Results.Ok(new { secret = "s3cr3t" });
        });
        await target.StartAsync(Ct);
        var address = target.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        await using var host = await WorkflowTestHost.StartAsync([HttpDefinition($"{address}/start")]);
        var instance = await host.StartInstanceAsync("http-guard");

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted, "a 3xx is not a success status");
        internalHits.Should().Be(0, "the redirect target must never be requested");
        (await host.IncidentsAsync(instance.Id)).Single().Error.Should().Contain("302");
        (await host.RowAsync(instance.Id)).Data.Should().NotContain("s3cr3t");

        await target.StopAsync(Ct);
    }
}
