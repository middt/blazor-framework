using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Rebus;
using Middt.Framework.Tests.Workflow;
using Rebus.Bus;
using Rebus.Transport.InMem;

namespace Middt.Framework.Tests.Messaging;

public sealed class RebusWorkflowTaskTests
{
    private const string RebusPublishWorkflow = """
        {
          "key": "rebus-pub-wf",
          "start": { "key": "start", "target": "pub" },
          "states": [
            { "key": "pub", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rebus",
                  "config": {
                    "action": "publish",
                    "type": "OrderPlaced",
                    "payload": { "orderId": 42 },
                    "into": "result"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string RebusSendWorkflow = """
        {
          "key": "rebus-send-wf",
          "start": { "key": "start", "target": "send" },
          "states": [
            { "key": "send", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rebus-send",
                  "config": {
                    "queue": "shipping-queue",
                    "type": "ShipOrder",
                    "payload": { "orderId": 77 },
                    "into": "result"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string RabbitMqRebusWorkflow = """
        {
          "key": "rabbit-rebus-wf",
          "start": { "key": "start", "target": "pub" },
          "states": [
            { "key": "pub", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rabbitmq",
                  "config": {
                    "exchange": "events",
                    "routingKey": "order.created",
                    "payload": { "id": 105 },
                    "into": "pubResult"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string RebusSendLocalWorkflow = """
        {
          "key": "rebus-local-wf",
          "start": { "key": "start", "target": "local" },
          "states": [
            { "key": "local", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rebus-send-local",
                  "config": {
                    "type": "ShipOrder",
                    "payload": { "orderId": 88 },
                    "into": "result"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string RebusSubscribeWorkflow = """
        {
          "key": "rebus-sub-wf",
          "start": { "key": "start", "target": "sub" },
          "states": [
            { "key": "sub", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rebus-subscribe",
                  "config": {
                    "topic": "orders.topic",
                    "into": "result"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string RebusUnsubscribeWorkflow = """
        {
          "key": "rebus-unsub-wf",
          "start": { "key": "start", "target": "unsub" },
          "states": [
            { "key": "unsub", "type": "Finish", "subType": "Success",
              "onEntry": [
                {
                  "task": "rebus-unsubscribe",
                  "config": {
                    "topic": "orders.topic",
                    "into": "result"
                  }
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task Rebus_task_publishes_strongly_typed_event_to_subscriber()
    {
        var network = new InMemNetwork();
        var sink = new MessageSink();

        // 1. Start subscriber endpoint
        var subBuilder = Host.CreateApplicationBuilder();
        subBuilder.Services.AddSingleton(network);
        subBuilder.Services.AddSingleton(sink);
        subBuilder.Services.AddMiddtRebus(
            s =>
            {
                s.InputQueueName = "sub-queue";
                s.UseInMemoryTransport = true;
            },
            o => o.Subscribe<OrderPlaced>());
        subBuilder.Services.AddMiddtRebusHandlersFromAssemblyOf<RebusPluginTests>();
        using var subHost = subBuilder.Build();
        await subHost.StartAsync(TestContext.Current.CancellationToken);

        // 2. Start workflow host with Rebus enabled
        await using var wfHost = await WorkflowTestHost.StartAsync(
            [RebusPublishWorkflow],
            services: s =>
            {
                s.AddSingleton(network);
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-pub-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        var instance = await wfHost.StartInstanceAsync("rebus-pub-wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.Data["result"]!["success"]!.GetValue<bool>().Should().BeTrue();
        instance.Data["result"]!["action"]!.GetValue<string>().Should().Be("publish");

        await sink.WaitForAsync(1, TimeSpan.FromSeconds(5));
        sink.Received.Should().ContainSingle().Which.Should().Be(new OrderPlaced(42));
    }

    [Fact]
    public async Task Rebus_task_sends_command_to_destination_queue()
    {
        var network = new InMemNetwork();
        var sink = new MessageSink();

        // 1. Start receiver on shipping-queue
        var shipBuilder = Host.CreateApplicationBuilder();
        shipBuilder.Services.AddSingleton(network);
        shipBuilder.Services.AddSingleton(sink);
        shipBuilder.Services.AddMiddtRebus(
            s =>
            {
                s.InputQueueName = "shipping-queue";
                s.UseInMemoryTransport = true;
            });
        shipBuilder.Services.AddMiddtRebusHandlersFromAssemblyOf<RebusPluginTests>();
        using var shipHost = shipBuilder.Build();
        await shipHost.StartAsync(TestContext.Current.CancellationToken);

        // 2. Start workflow host with Rebus enabled
        await using var wfHost = await WorkflowTestHost.StartAsync(
            [RebusSendWorkflow],
            services: s =>
            {
                s.AddSingleton(network);
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-send-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        var instance = await wfHost.StartInstanceAsync("rebus-send-wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.Data["result"]!["success"]!.GetValue<bool>().Should().BeTrue();
        instance.Data["result"]!["action"]!.GetValue<string>().Should().Be("send");

        await sink.WaitForAsync(1, TimeSpan.FromSeconds(5));
        sink.Received.Should().ContainSingle().Which.Should().Be(new ShipOrder(77));
    }

    [Fact]
    public async Task Rabbitmq_task_publishes_via_Rebus_when_Rebus_is_registered()
    {
        var network = new InMemNetwork();

        await using var wfHost = await WorkflowTestHost.StartAsync(
            [RabbitMqRebusWorkflow],
            services: s =>
            {
                s.AddSingleton(network);
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-rabbit-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        var instance = await wfHost.StartInstanceAsync("rabbit-rebus-wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.Data["pubResult"]!["published"]!.GetValue<bool>().Should().BeTrue();
        instance.Data["pubResult"]!["exchange"]!.GetValue<string>().Should().Be("events");
        instance.Data["pubResult"]!["routingKey"]!.GetValue<string>().Should().Be("order.created");
    }

    [Fact]
    public async Task Rebus_task_send_local_executes_successfully()
    {
        var network = new InMemNetwork();

        await using var wfHost = await WorkflowTestHost.StartAsync(
            [RebusSendLocalWorkflow],
            services: s =>
            {
                s.AddSingleton(network);
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-local-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        var instance = await wfHost.StartInstanceAsync("rebus-local-wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.Data["result"]!["success"]!.GetValue<bool>().Should().BeTrue();
        instance.Data["result"]!["action"]!.GetValue<string>().Should().Be("sendLocal");
    }

    [Fact]
    public async Task Rebus_task_subscribes_and_unsubscribes_to_topic()
    {
        var network = new InMemNetwork();

        await using var wfHost = await WorkflowTestHost.StartAsync(
            [RebusSubscribeWorkflow, RebusUnsubscribeWorkflow],
            services: s =>
            {
                s.AddSingleton(network);
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-sub-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        var subInstance = await wfHost.StartInstanceAsync("rebus-sub-wf");
        subInstance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        subInstance.Data["result"]!["success"]!.GetValue<bool>().Should().BeTrue();
        subInstance.Data["result"]!["action"]!.GetValue<string>().Should().Be("subscribe");
        subInstance.Data["result"]!["topic"]!.GetValue<string>().Should().Be("orders.topic");

        var unsubInstance = await wfHost.StartInstanceAsync("rebus-unsub-wf");
        unsubInstance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        unsubInstance.Data["result"]!["success"]!.GetValue<bool>().Should().BeTrue();
        unsubInstance.Data["result"]!["action"]!.GetValue<string>().Should().Be("unsubscribe");
        unsubInstance.Data["result"]!["topic"]!.GetValue<string>().Should().Be("orders.topic");
    }

    [Theory]
    [InlineData("""{ "action": "defer", "payload": { "id": 1 } }""", "rebus: 'delay'")]
    [InlineData("""{ "action": "send" }""", "rebus: 'payload' (or 'message' / 'body') is required.")]
    [InlineData("""{ "action": "subscribe" }""", "rebus: 'topic' or 'type' is required")]
    public async Task Rebus_task_validates_required_configuration(string config, string expectedError)
    {
        var definition = $$"""
            { "key": "bad-rebus", "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "onEntry": [ { "task": "rebus", "config": {{config}} } ] } ] }
            """;

        var act = () => WorkflowTestHost.StartAsync(
            [definition],
            services: s =>
            {
                s.AddSingleton(new InMemNetwork());
                s.AddMiddtRebus(settings =>
                {
                    settings.InputQueueName = "wf-val-queue";
                    settings.UseInMemoryTransport = true;
                });
            });

        (await act.Should().ThrowAsync<Middt.Framework.Workflow.Runtime.WorkflowDefinitionException>())
            .Which.Errors.Should().Contain(e => e.Contains(expectedError, StringComparison.Ordinal));
    }
}
