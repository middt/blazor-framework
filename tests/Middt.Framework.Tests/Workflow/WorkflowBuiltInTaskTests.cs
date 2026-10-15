using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Tests.Infrastructure;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowBuiltInTaskTests
{
    private const string HttpWorkflow = """
        {
          "key": "http-wf",
          "start": { "key": "start", "target": "calling",
                     "input": { "schema": { "type": "object", "properties": { "id": { "type": "string" }, "amount": { "type": "number" } } } } },
          "states": [
            { "key": "calling",
              "onEntry": [ { "task": "http", "key": "score",
                "config": { "method": "post", "url": "https://scoring.test/api/customers/{{ data.id }}/score?amount={{ data.amount }}",
                            "headers": { "X-Static": "yes", "X-Customer": "{{ data.id }}" },
                            "body": { "customer": "{{ data.id }}", "amount": "{{ data.amount }}", "fixed": [ 1, "two" ] },
                            "into": "score" } } ] }
          ]
        }
        """;

    private const string HttpGetWorkflow = """
        {
          "key": "http-get",
          "start": { "key": "start", "target": "calling" },
          "states": [
            { "key": "calling",
              "onEntry": [ { "task": "http", "config": { "url": "https://scoring.test/ping", "acceptedStatusCodes": [ 404 ], "into": "ping" } } ] }
          ]
        }
        """;

    private const string PublishWorkflow = """
        {
          "key": "publish-wf",
          "start": { "key": "start", "target": "approved",
                     "input": { "schema": { "type": "object", "properties": { "orderId": { "type": "integer" } } } } },
          "states": [
            { "key": "approved", "type": "Finish", "subType": "Success",
              "onEntry": [ { "task": "publish", "config": { "type": "OrderApproved", "payload": { "orderId": "{{ data.orderId }}", "label": "order {{ data.orderId }}" } } } ] }
          ]
        }
        """;

    private static async Task<(WorkflowTestHost Host, FakeHttpHandler Handler)> HttpHostAsync()
    {
        var handler = new FakeHttpHandler();
        var host = await WorkflowTestHost.StartAsync(
            [HttpWorkflow, HttpGetWorkflow],
            services: s => s.AddHttpClient(HttpTask.DefaultClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        return (host, handler);
    }

    [Fact]
    public async Task Http_task_sends_method_url_headers_and_rendered_body_and_stores_the_response()
    {
        var (host, handler) = await HttpHostAsync();
        await using var _ = host;
        handler.ResponseBody = """{ "score": 91 }""";

        var instance = await host.StartInstanceAsync("http-wf", new { id = "c-1", amount = 12.5 });

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.AbsoluteUri.Should().Be("https://scoring.test/api/customers/c-1/score?amount=12.5");
        request.Headers["X-Static"].Should().Be("yes");
        request.Headers["X-Customer"].Should().Be("c-1");
        request.Headers["Content-Type"].Should().StartWith("application/json");
        var body = JsonNode.Parse(request.Body!)!;
        body["customer"]!.GetValue<string>().Should().Be("c-1");
        body["amount"]!.GetValue<decimal>().Should().Be(12.5m);
        body["fixed"]!.ToJsonString().Should().Be("""[1,"two"]""");

        instance.Data["score"]!["statusCode"]!.GetValue<int>().Should().Be(200);
        instance.Data["score"]!["body"]!["score"]!.GetValue<int>().Should().Be(91);
    }

    [Theory]
    [InlineData("a/../../admin", "a%2F..%2F..%2Fadmin")]
    [InlineData("x?admin=true&y=", "x%3Fadmin%3Dtrue%26y%3D")]
    [InlineData("evil.com#frag", "evil.com%23frag")]
    [InlineData("a b", "a%20b")]
    public async Task Http_task_escapes_placeholder_values_in_the_url(string id, string escaped)
    {
        var (host, handler) = await HttpHostAsync();
        await using var _ = host;

        await host.StartInstanceAsync("http-wf", new { id, amount = 1 });

        var uri = handler.Requests.Should().ContainSingle().Subject.Uri;
        uri.Host.Should().Be("scoring.test");
        uri.OriginalString.Should().Be($"https://scoring.test/api/customers/{escaped}/score?amount=1");
        uri.Query.Should().Be("?amount=1");
    }

    [Fact]
    public async Task Http_task_with_a_non_accepted_status_fails_the_task()
    {
        var (host, handler) = await HttpHostAsync();
        await using var _ = host;
        handler.StatusCode = HttpStatusCode.InternalServerError;

        var instance = await host.StartInstanceAsync("http-wf", new { id = "c-1", amount = 1 });

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.Data.ContainsKey("score").Should().BeFalse();
        var incident = (await host.IncidentsAsync(instance.Id)).Should().ContainSingle().Subject;
        incident.TaskKey.Should().Be("score");
        incident.Error.Should().Contain("returned 500").And.NotContain("amount=", "the query string is not logged");
    }

    [Fact]
    public async Task Http_task_honours_accepted_status_codes_default_get_and_non_json_bodies()
    {
        var (host, handler) = await HttpHostAsync();
        await using var _ = host;
        handler.StatusCode = HttpStatusCode.NotFound;
        handler.ResponseBody = "not json";

        var instance = await host.StartInstanceAsync("http-get");

        instance.Status.Should().Be(WorkflowInstanceStatus.Active);
        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().Body.Should().BeNull();
        instance.Data["ping"]!["statusCode"]!.GetValue<int>().Should().Be(404);
        instance.Data["ping"]!["body"]!.GetValue<string>().Should().Be("not json");
    }

    [Fact]
    public async Task Publish_task_sends_the_rendered_payload_with_workflow_and_instance()
    {
        var publisher = new FakePublisher();
        await using var host = await WorkflowTestHost.StartAsync([PublishWorkflow], services: s => s.AddSingleton<IWorkflowMessagePublisher>(publisher));

        var instance = await host.StartInstanceAsync("publish-wf", new { orderId = 42 });

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        var (type, payload) = publisher.Published.Should().ContainSingle().Subject;
        type.Should().Be("OrderApproved");
        payload["orderId"]!.GetValue<long>().Should().Be(42);
        payload["label"]!.GetValue<string>().Should().Be("order 42");
        payload["workflow"]!.GetValue<string>().Should().Be("publish-wf");
        payload["instanceId"]!.GetValue<Guid>().Should().Be(instance.Id);
    }

    [Fact]
    public async Task Templates_render_the_instance_id()
    {
        // instance.id is a Guid in the expression scope: templates must render its text instead of faulting the task.
        const string definition = """
            {
              "key": "instance-id-wf",
              "start": { "key": "start", "target": "done" },
              "states": [
                { "key": "done", "type": "Finish", "subType": "Success",
                  "onEntry": [ { "task": "publish", "config": { "type": "Started", "payload": { "ref": "wf-{{ instance.id }}" } } } ] }
              ]
            }
            """;
        var publisher = new FakePublisher();
        await using var host = await WorkflowTestHost.StartAsync([definition], services: s => s.AddSingleton<IWorkflowMessagePublisher>(publisher));

        var instance = await host.StartInstanceAsync("instance-id-wf", new { });

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        publisher.Published.Should().ContainSingle().Subject.Payload["ref"]!.GetValue<string>().Should().Be($"wf-{instance.Id}");
    }

    [Fact]
    public async Task Publish_task_without_a_publisher_fails_the_task()
    {
        await using var host = await WorkflowTestHost.StartAsync(PublishWorkflow);

        var instance = await host.StartInstanceAsync("publish-wf", new { orderId = 42 });

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.State.Should().BeEmpty("the start hop was rolled back");
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle().Which.Error.Should().Contain("IWorkflowMessagePublisher");
    }

    [Fact]
    public async Task Log_task_writes_the_rendered_message_at_the_configured_level()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await WorkflowTestHost.StartAsync(
            ["""
             { "key": "log-wf", "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object" } } },
               "states": [ { "key": "a", "onEntry": [
                 { "task": "log", "config": { "message": "Hello {{ data.name }}", "level": "Warning" } },
                 { "task": "log", "config": { "message": "plain" } } ] } ] }
             """],
            services: s => s.AddSingleton<ILoggerProvider>(logs));

        var instance = await host.StartInstanceAsync("log-wf", new { name = "Ann" });

        instance.Status.Should().Be(WorkflowInstanceStatus.Active);
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message == $"[log-wf {instance.Id}] Hello Ann");
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.EndsWith("plain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Set_task_evaluates_expressions_into_nested_paths()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "set-wf", "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object" } } },
              "states": [ { "key": "a", "onEntry": [ { "task": "set", "config": { "values": {
                  "totals.net": "data.price * data.qty", "totals.label": "\"x\" + data.qty", "stamp": "now", "who": "user.Name" } } } ] } ] }
            """);

        var instance = await host.StartInstanceAsync("set-wf", new { price = 2.5, qty = 4 }, user: WorkflowTestHost.User("ann"));

        instance.Data["totals"]!["net"]!.GetValue<decimal>().Should().Be(10m);
        instance.Data["totals"]!["label"]!.GetValue<string>().Should().Be("x4");
        instance.Data["stamp"]!.GetValue<DateTimeOffset>().Should().Be(WorkflowTestHost.StartTime);
        instance.Data["who"]!.GetValue<string>().Should().Be("ann");
    }

    private const string KeyedWorkflow = """
        { "key": "keyed",
          "start": { "key": "start", "target": "draft",
                     "input": { "schema": { "type": "object", "properties": { "id": { "type": "integer" }, "other": { "type": "integer" } } } } },
          "states": [
            { "key": "draft", "transitions": [
                { "key": "create", "target": "active", "onExecute": [ { "task": "setKey", "config": { "key": "record-{{ data.id }}" } } ] },
                { "key": "create-other", "target": "active", "onExecute": [ { "task": "setKey", "config": { "key": "record-{{ data.other }}" } } ] },
                { "key": "create-broken", "target": "active", "onExecute": [
                    { "task": "setKey", "config": { "key": "record-{{ data.id }}" } }, { "task": "fail", "config": { "message": "later task failed" } } ] } ] },
            { "key": "active", "transitions": [
                { "key": "again", "target": "$self", "onExecute": [ { "task": "setKey", "config": { "key": "record-{{ data.id }}" } } ] },
                { "key": "rename", "target": "$self", "onExecute": [ { "task": "setKey", "config": { "key": "renamed-{{ data.id }}" } } ] } ] }
          ] }
        """;

    [Fact]
    public async Task Set_key_gives_the_instance_its_business_key_and_a_start_with_that_key_returns_it()
    {
        await using var host = await WorkflowTestHost.StartAsync(KeyedWorkflow);

        var instance = await host.StartInstanceAsync("keyed", new { id = 7 });
        instance.Key.Should().BeNull();

        instance = await host.TransitionAsync(instance.Id, "create");
        instance.Key.Should().Be("record-7");
        (await host.RowAsync(instance.Id)).BusinessKey.Should().Be("record-7");

        instance = await host.TransitionAsync(instance.Id, "again");
        instance.Status.Should().Be(WorkflowInstanceStatus.Active, "the same key again is a no-op");

        var (existing, created) = await host.StartWithFlagAsync("keyed", new { id = 99 }, key: "record-7");
        created.Should().BeFalse();
        existing.Id.Should().Be(instance.Id);
    }

    [Fact]
    public async Task Set_key_with_a_different_key_than_the_existing_one_fails_the_task()
    {
        await using var host = await WorkflowTestHost.StartAsync(KeyedWorkflow);
        var instance = await host.StartInstanceAsync("keyed", new { id = 7 }, key: "given");

        instance = await host.TransitionAsync(instance.Id, "create");

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.State.Should().Be("draft");
        instance.Key.Should().Be("given");
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle().Which.Error.Should().Contain("already has the business key 'given'");
    }

    [Fact]
    public async Task Set_key_already_used_by_another_instance_fails_with_a_clear_error()
    {
        await using var host = await WorkflowTestHost.StartAsync(KeyedWorkflow);
        var first = await host.StartInstanceAsync("keyed", new { id = 7 });
        await host.TransitionAsync(first.Id, "create");
        var second = await host.StartInstanceAsync("keyed", new { id = 8, other = 7 });

        second = await host.TransitionAsync(second.Id, "create-other");

        second.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        second.Key.Should().BeNull();
        (await host.IncidentsAsync(second.Id)).Should().ContainSingle().Which.Error.Should()
            .Contain("business key 'record-7' is already used by another instance of 'keyed'");
    }

    [Fact]
    public async Task A_failed_hop_rolls_the_key_back()
    {
        await using var host = await WorkflowTestHost.StartAsync(KeyedWorkflow);
        var instance = await host.StartInstanceAsync("keyed", new { id = 7 });

        instance = await host.TransitionAsync(instance.Id, "create-broken");

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.Key.Should().BeNull();
        (await host.RowAsync(instance.Id)).BusinessKey.Should().BeNull();
        (await host.StartWithFlagAsync("keyed", new { id = 7 }, key: "record-7")).Created.Should().BeTrue("the key was never committed");
    }

    [Theory]
    [InlineData("""{ }""", "setKey: 'key' (text template) is required.")]
    [InlineData("""{ "key": "  " }""", "setKey: 'key' (text template) is required.")]
    [InlineData("""{ "key": 5 }""", "setKey: 'key' (text template) is required.")]
    public async Task Set_key_config_is_validated_when_definitions_load(string config, string error)
    {
        var definition = $$"""
            { "key": "bad-key", "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "onEntry": [ { "task": "setKey", "config": {{config}} } ] } ] }
            """;

        var act = () => WorkflowTestHost.StartAsync(definition);

        (await act.Should().ThrowAsync<Middt.Framework.Workflow.Runtime.WorkflowDefinitionException>()).Which.Errors.Should().Contain(e => e.EndsWith(error, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Set_key_template_errors_are_reported_when_definitions_load()
    {
        const string definition = """
            { "key": "bad-template", "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "onEntry": [ { "task": "setKey", "config": { "key": "x-{{ data.( }}" } } ] } ] }
            """;

        var act = () => WorkflowTestHost.StartAsync(definition);

        (await act.Should().ThrowAsync<Middt.Framework.Workflow.Runtime.WorkflowDefinitionException>()).Which.Errors.Should().Contain(e => e.Contains("setKey: ", StringComparison.Ordinal));
    }
}
