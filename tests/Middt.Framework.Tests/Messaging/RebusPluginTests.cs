using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Middt.Framework.Rebus;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace Middt.Framework.Tests.Messaging;

public sealed record OrderPlaced(int OrderId);

public sealed record ShipOrder(int OrderId);

public sealed record PoisonMessage(int Id);

/// <summary>Collects every message delivered to the test handlers.</summary>
public sealed class MessageSink : IDisposable
{
    private readonly ConcurrentQueue<object> received = new();
    private readonly SemaphoreSlim signal = new(0);

    public IReadOnlyCollection<object> Received => received;

    private int poisonAttempts;

    public int PoisonAttempts => Volatile.Read(ref poisonAttempts);

    public void RecordPoisonAttempt() => Interlocked.Increment(ref poisonAttempts);

    public void Dispose() => signal.Dispose();

    public void Add(object message)
    {
        received.Enqueue(message);
        signal.Release();
    }

    public async Task WaitForAsync(int count, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (received.Count < count)
        {
            await signal.WaitAsync(cts.Token);
        }
    }
}

public sealed class OrderPlacedHandler(MessageSink sink) : IHandleMessages<OrderPlaced>
{
    public Task Handle(OrderPlaced message)
    {
        sink.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class ShipOrderHandler(MessageSink sink) : IHandleMessages<ShipOrder>
{
    public Task Handle(ShipOrder message)
    {
        sink.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class PoisonMessageHandler(MessageSink sink) : IHandleMessages<PoisonMessage>
{
    public Task Handle(PoisonMessage message)
    {
        sink.RecordPoisonAttempt();
        throw new InvalidOperationException("handler always fails");
    }
}

public sealed class RebusPluginTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(InMemNetwork network, string queue, Action<MiddtRebusOptions>? configure = null, Action<RebusSettings>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(network);
        builder.Services.AddSingleton<MessageSink>();
        builder.Services.AddMiddtRebus(
            s =>
            {
                s.InputQueueName = queue;
                s.UseInMemoryTransport = true;
                s.MaxDeliveryAttempts = 2;
                settings?.Invoke(s);
            },
            configure);
        builder.Services.AddMiddtRebusHandlersFromAssemblyOf<RebusPluginTests>();

        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    [Fact]
    public async Task Published_event_reaches_a_subscribed_endpoint()
    {
        var network = new InMemNetwork();
        using var subscriber = await StartAsync(network, "subscriber", o => o.Subscribe<OrderPlaced>());
        using var publisher = await StartAsync(network, "publisher");

        await publisher.Services.GetRequiredService<IBus>().Publish(new OrderPlaced(42));

        var sink = subscriber.Services.GetRequiredService<MessageSink>();
        await sink.WaitForAsync(1, Timeout);
        sink.Received.Should().ContainSingle().Which.Should().Be(new OrderPlaced(42));
    }

    [Fact]
    public async Task Send_uses_code_based_route()
    {
        var network = new InMemNetwork();
        using var shipping = await StartAsync(network, "shipping");
        using var sales = await StartAsync(network, "sales", o => o.Map<ShipOrder>("shipping"));

        await sales.Services.GetRequiredService<IBus>().Send(new ShipOrder(7));

        var sink = shipping.Services.GetRequiredService<MessageSink>();
        await sink.WaitForAsync(1, Timeout);
        sink.Received.Should().ContainSingle().Which.Should().Be(new ShipOrder(7));
    }

    [Fact]
    public async Task Send_uses_configuration_based_route()
    {
        var network = new InMemNetwork();
        using var shipping = await StartAsync(network, "shipping");
        using var sales = await StartAsync(network, "sales", settings: s => s.Routes[typeof(ShipOrder).AssemblyQualifiedName!] = "shipping");

        await sales.Services.GetRequiredService<IBus>().Send(new ShipOrder(8));

        await shipping.Services.GetRequiredService<MessageSink>().WaitForAsync(1, Timeout);
    }

    [Fact]
    public async Task Failing_message_is_retried_then_moved_to_the_error_queue()
    {
        var network = new InMemNetwork();
        using var host = await StartAsync(network, "worker", settings: s => s.ErrorQueueName = "worker-error");

        await host.Services.GetRequiredService<IBus>().SendLocal(new PoisonMessage(1));

        var deadline = DateTime.UtcNow + Timeout;
        while (network.Count("worker-error") == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Ct);
        }

        network.Count("worker-error").Should().Be(1);
        host.Services.GetRequiredService<MessageSink>().PoisonAttempts.Should().Be(2, "MaxDeliveryAttempts is 2");
    }

    [Fact]
    public async Task Payload_is_plain_json_without_type_name_metadata()
    {
        var network = new InMemNetwork();
        using var host = await StartAsync(network, "sender", o => o.Map<ShipOrder>("inspected"));
        network.CreateQueue("inspected");

        await host.Services.GetRequiredService<IBus>().Send(new ShipOrder(9));

        var message = network.GetNextOrNull("inspected");
        message.Should().NotBeNull();
        var body = System.Text.Encoding.UTF8.GetString(message!.Body);
        body.Should().Contain("9").And.NotContain("$type");
    }

    [Fact]
    public void Missing_connection_string_fails_at_startup_unless_in_memory()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMiddtRebus(s => s.InputQueueName = "queue");
        using var host = builder.Build();

        var act = () => host.Services.GetRequiredService<IOptions<RebusSettings>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*");
    }

    [Fact]
    public void Missing_queue_name_fails_validation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMiddtRebus(s => s.UseInMemoryTransport = true);
        using var host = builder.Build();

        var act = () => host.Services.GetRequiredService<IOptions<RebusSettings>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*InputQueueName*");
    }
}
