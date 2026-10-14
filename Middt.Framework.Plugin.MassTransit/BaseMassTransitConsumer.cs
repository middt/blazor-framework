using MassTransit;

namespace Middt.Framework.Plugin.MassTransit;

/// <summary>
/// Base consumer for <typeparamref name="TMessage"/>. Register derived types with
/// <c>x.AddConsumer&lt;TConsumer&gt;()</c> in the <c>AddMiddtMassTransit</c> configure callback.
/// </summary>
public abstract class BaseMassTransitConsumer<TMessage> : IConsumer<TMessage>
    where TMessage : class
{
    public abstract Task Consume(ConsumeContext<TMessage> context);
}
