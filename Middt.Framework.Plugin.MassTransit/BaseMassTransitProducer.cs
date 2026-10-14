using MassTransit;

namespace Middt.Framework.Plugin.MassTransit;

/// <summary>Sends <typeparamref name="TCommand"/> messages to the queue named <see cref="QueueName"/>.</summary>
public abstract class BaseMassTransitProducer<TCommand>(ISendEndpointProvider sendEndpointProvider)
    where TCommand : class
{
    public abstract string QueueName { get; }

    public virtual async Task SendAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{QueueName}")).ConfigureAwait(false);
        await endpoint.Send(message, cancellationToken).ConfigureAwait(false);
    }
}
