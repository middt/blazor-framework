using System.Net;
using System.Text.Json.Nodes;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowMessageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal const string Order = """
        {
          "key": "order",
          "start": { "key": "start", "target": "awaitingPayment",
            "input": { "schema": { "type": "object", "properties": { "total": { "type": "number" } } } } },
          "states": [
            { "key": "awaitingPayment", "transitions": [
                { "key": "paid", "target": "paid", "trigger": "Event", "event": "payment.received", "rule": "input.amount >= data.total",
                  "input": { "schema": { "type": "object", "required": [ "amount" ], "properties": { "amount": { "type": "number" } } }, "into": "payment" } },
                { "key": "partial", "target": "$self", "trigger": "Event", "event": "payment.received",
                  "input": { "schema": { "type": "object", "properties": { "amount": { "type": "number" } } }, "into": "partialPayment" } },
                { "key": "refund", "target": "refunded", "trigger": "Event", "event": "refund.requested", "roles": [ "payments" ] },
                { "key": "hold", "target": "onHold" } ] },
            { "key": "onHold", "transitions": [ { "key": "resume", "target": "awaitingPayment" } ] },
            { "key": "paid", "type": "Finish", "subType": "Success" },
            { "key": "refunded", "type": "Finish", "subType": "Cancelled" }
          ]
        }
        """;

    private static async Task<(WorkflowTestHost Host, WorkflowInstanceModel Instance)> StartOrderAsync(string key = "ORD-1", decimal total = 100)
    {
        var host = await WorkflowTestHost.StartAsync(Order);
        var instance = await host.StartInstanceAsync("order", new { total }, key);
        return (host, instance);
    }

    private static Task<WorkflowMessageResult> SendAsync(WorkflowTestHost host, WorkflowMessage message, Middt.Framework.Workflow.Expressions.WorkflowUser? user = null) =>
        host.WithEngineAsync(e => e.DeliverMessageAsync(message, user ?? WorkflowTestHost.User(), Ct));

    private static WorkflowMessage Payment(decimal amount, string key = "ORD-1", TimeSpan bufferFor = default) =>
        new() { Name = "payment.received", Workflow = "order", Key = key, Data = new JsonObject { ["amount"] = amount }, BufferFor = bufferFor };

    [Fact]
    public async Task A_message_correlated_by_business_key_runs_the_event_transition_whose_rule_holds()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;

        var partial = await SendAsync(host, Payment(40));
        partial.Outcome.Should().Be(WorkflowMessageOutcome.Delivered);
        partial.Instance!.State.Should().Be("awaitingPayment");
        partial.Instance.Data["partialPayment"]!["amount"]!.GetValue<decimal>().Should().Be(40);

        var paid = await SendAsync(host, Payment(100));
        paid.Instance!.State.Should().Be("paid");
        paid.Instance.Data["payment"]!["amount"]!.GetValue<decimal>().Should().Be(100);

        var history = await host.HistoryAsync(started.Id);
        history.Select(h => (h.TransitionKey, h.Trigger)).Should().Equal(("start", "Start"), ("partial", "Event"), ("paid", "Event"));
    }

    [Fact]
    public async Task A_message_can_be_correlated_by_instance_id()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;

        var result = await SendAsync(host, new WorkflowMessage { Name = "payment.received", InstanceId = started.Id, Data = new JsonObject { ["amount"] = 150 } });

        result.Instance!.State.Should().Be("paid");
    }

    [Fact]
    public async Task Without_buffering_a_message_the_instance_is_not_waiting_for_is_rejected()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;
        await host.TransitionAsync(started.Id, "hold");

        var act = () => SendAsync(host, Payment(100));

        await act.Should().ThrowAsync<WorkflowConflictException>().WithMessage("*cannot take 'payment.received'*");
        (await host.JobsAsync(started.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_buffered_message_is_delivered_as_soon_as_the_instance_enters_a_state_waiting_for_it()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;
        await host.TransitionAsync(started.Id, "hold");

        var result = await SendAsync(host, Payment(100, bufferFor: TimeSpan.FromMinutes(10)));

        result.Outcome.Should().Be(WorkflowMessageOutcome.Buffered);
        var job = (await host.JobsAsync(started.Id)).Should().ContainSingle().Which;
        job.Kind.Should().Be(WorkflowJobKind.Message);
        job.DueAt.Should().Be(WorkflowTestHost.StartTime.UtcDateTime.AddMinutes(10), "it waits for a matching state, at the latest until it expires");
        (await host.RunJobsAsync()).Should().Be(0);

        await host.TransitionAsync(started.Id, "resume");
        (await host.JobsAsync(started.Id)).Single().DueAt.Should().Be(WorkflowTestHost.StartTime.UtcDateTime, "entering awaitingPayment wakes it");

        await host.RunJobsAsync();
        var instance = await host.GetAsync(started.Id);
        instance.State.Should().Be("paid");
        (await host.JobsAsync(started.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_buffered_message_expires()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;
        await host.TransitionAsync(started.Id, "hold");
        await SendAsync(host, Payment(100, bufferFor: TimeSpan.FromMinutes(1)));

        (await host.RunJobsAsync(TimeSpan.FromMinutes(2))).Should().Be(1);
        (await host.JobsAsync(started.Id)).Should().BeEmpty();

        await host.TransitionAsync(started.Id, "resume");
        await host.RunJobsAsync();
        (await host.GetAsync(started.Id)).State.Should().Be("awaitingPayment");
    }

    [Fact]
    public async Task A_message_for_a_busy_instance_is_buffered_and_retried()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;
        await host.SetStatusAsync(started.Id, WorkflowInstanceStatus.Busy, WorkflowTestHost.StartTime.UtcDateTime);

        var result = await SendAsync(host, Payment(100, bufferFor: TimeSpan.FromMinutes(10)));
        result.Outcome.Should().Be(WorkflowMessageOutcome.Buffered);
        await host.RunJobsAsync();
        (await host.JobsAsync(started.Id)).Should().ContainSingle("still busy: retried with backoff");

        await host.SetStatusAsync(started.Id, WorkflowInstanceStatus.Active);
        await host.RunJobsAsync(TimeSpan.FromSeconds(30));
        (await host.GetAsync(started.Id)).State.Should().Be("paid");
    }

    [Fact]
    public async Task Transition_roles_apply_to_the_sender()
    {
        var (host, _) = await StartOrderAsync();
        await using var __ = host;
        var refund = new WorkflowMessage { Name = "refund.requested", Workflow = "order", Key = "ORD-1" };

        var denied = () => SendAsync(host, refund, WorkflowTestHost.User("alice"));
        await denied.Should().ThrowAsync<WorkflowForbiddenException>();

        (await SendAsync(host, refund, WorkflowTestHost.User("pat", "payments"))).Instance!.State.Should().Be("refunded");
    }

    [Fact]
    public async Task Invalid_messages_are_rejected()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;

        await ((Func<Task>)(() => SendAsync(host, new WorkflowMessage { Name = "payment.received", Workflow = "order" })))
            .Should().ThrowAsync<WorkflowValidationException>();
        await ((Func<Task>)(() => SendAsync(host, new WorkflowMessage { Name = "payment.received", InstanceId = started.Id, Key = "ORD-1", Workflow = "order" })))
            .Should().ThrowAsync<WorkflowValidationException>();
        await ((Func<Task>)(() => SendAsync(host, new WorkflowMessage { Name = "unknown", InstanceId = started.Id })))
            .Should().ThrowAsync<WorkflowConflictException>().WithMessage("*no transition for message 'unknown'*");
        await ((Func<Task>)(() => SendAsync(host, Payment(1, key: "missing"))))
            .Should().ThrowAsync<WorkflowNotFoundException>();
        await ((Func<Task>)(() => SendAsync(host, new WorkflowMessage { Name = "payment.received", InstanceId = started.Id, Data = new JsonObject { ["amount"] = "x" } })))
            .Should().ThrowAsync<WorkflowValidationException>("the body is validated by the transition's input schema");
    }

    [Fact]
    public async Task Event_transitions_are_not_offered_as_manual_transitions()
    {
        var (host, started) = await StartOrderAsync();
        await using var _ = host;

        (await host.GetAsync(started.Id, WorkflowTestHost.User("pat", "payments"))).AvailableTransitions.Select(t => t.Key).Should().Equal("hold");
        var act = () => host.TransitionAsync(started.Id, "paid", new { amount = 100 });
        await act.Should().ThrowAsync<WorkflowConflictException>();
    }

    [Fact]
    public async Task Http_endpoints_deliver_and_buffer_messages()
    {
        var (host, started) = await StartOrderAsync("ORD-H");
        await using var _ = host;

        await host.TransitionAsync(started.Id, "hold");
        var buffered = await host.Client.SendAsync(host.Request(HttpMethod.Post, "api/workflows/order/messages/payment.received?key=ORD-H&bufferFor=PT5M", new { amount = 100 }), Ct);
        buffered.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var rejected = await host.Client.SendAsync(host.Request(HttpMethod.Post, $"api/workflows/order/instances/{started.Id}/messages/payment.received", new { amount = 100 }), Ct);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await host.TransitionAsync(started.Id, "resume");
        await host.RunJobsAsync();
        (await host.GetAsync(started.Id)).State.Should().Be("paid");

        var other = await host.StartInstanceAsync("order", new { total = 10 }, "ORD-I");
        var delivered = await host.Client.SendAsync(host.Request(HttpMethod.Post, $"api/workflows/order/instances/{other.Id}/messages/payment.received", new { amount = 10 }), Ct);
        delivered.StatusCode.Should().Be(HttpStatusCode.OK);

        var badDuration = await host.Client.SendAsync(host.Request(HttpMethod.Post, "api/workflows/order/messages/payment.received?key=ORD-I&bufferFor=soon", new { amount = 1 }), Ct);
        badDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("""{ "key": "e", "start": { "key": "s", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "x", "target": "b", "trigger": "Event" } ] }, { "key": "b", "type": "Finish" } ] }""", "event transitions need an 'event'")]
    [InlineData("""{ "key": "e", "start": { "key": "s", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "x", "target": "b", "event": "go" } ] }, { "key": "b", "type": "Finish" } ] }""", "'event' is only valid")]
    [InlineData("""{ "key": "e", "start": { "key": "s", "target": "a", "trigger": "Event", "event": "go" }, "states": [ { "key": "a", "type": "Finish" } ] }""", "start transition cannot be an event transition")]
    public async Task Invalid_event_definitions_fail_at_load(string definition, string error)
    {
        var act = () => WorkflowTestHost.StartAsync(definition);
        (await act.Should().ThrowAsync<WorkflowDefinitionException>()).Which.Message.Should().Contain(error);
    }
}
