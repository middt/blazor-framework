using System.Text.Json;
using Microsoft.Extensions.Logging;
using Middt.Framework.Common.Helper;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Middt.Framework.Plugin.RabbitMQ;

/// <summary>
/// Consumes JSON messages of type <typeparamref name="TMessage"/> from the configured queue.
/// Messages are acked after successful handling, nacked without requeue when they cannot be
/// deserialized and nacked with requeue when the handler fails.
/// </summary>
public abstract class MiddRabbitMqConsumerBase<TMessage>(IConnectionFactory connectionFactory, ExchangeTypes exchangeType, ILogger logger)
    : MiddtRabbitMqConnection(connectionFactory, exchangeType, logger)
{
    /// <summary>Raised for every successfully deserialized message, after <see cref="HandleAsync"/>.</summary>
    public event Func<TMessage, Task>? MessageReceived;

    /// <summary>Maximum number of unacknowledged messages delivered to this consumer.</summary>
    protected virtual ushort PrefetchCount => 10;

    public async Task StartConsumingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(Channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await Channel.BasicConsumeAsync(queue: RefinedQueueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await base.DeclareTopologyAsync(channel, cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: RefinedQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: RefinedQueueName,
            exchange: RefinedExchangeName,
            routingKey: RefinedRoutingKey,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Handles a message. Throwing causes the message to be requeued.</summary>
    protected virtual Task HandleAsync(TMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CA1031 // Exceptions must not escape the consumer callback; they are logged and translated to nacks.
    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel = ((AsyncEventingBasicConsumer)sender).Channel;
        var cancellationToken = args.CancellationToken;

        TMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(args.Body.Span, JsonHelper.Options);
            if (message is null)
            {
                throw new JsonException("Message body deserialized to null.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not deserialize message {DeliveryTag} from queue {Queue}; discarding", args.DeliveryTag, RefinedQueueName);
            await TryNackAsync(channel, args.DeliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await HandleAsync(message, cancellationToken).ConfigureAwait(false);

            if (MessageReceived is { } handlers)
            {
                foreach (var handler in handlers.GetInvocationList().Cast<Func<TMessage, Task>>())
                {
                    await handler(message).ConfigureAwait(false);
                }
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling message {DeliveryTag} from queue {Queue}; requeueing", args.DeliveryTag, RefinedQueueName);
            await TryNackAsync(channel, args.DeliveryTag, requeue: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryNackAsync(IChannel channel, ulong deliveryTag, bool requeue, CancellationToken cancellationToken)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not nack message {DeliveryTag} on queue {Queue}", deliveryTag, RefinedQueueName);
        }
    }
#pragma warning restore CA1031
}
