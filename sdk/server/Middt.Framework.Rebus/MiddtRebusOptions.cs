using Rebus.Config;

namespace Middt.Framework.Rebus;

/// <summary>Code based Rebus configuration for <see cref="RebusServiceCollectionExtensions.AddMiddtRebus(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, Action{MiddtRebusOptions}?)"/>.</summary>
public sealed class MiddtRebusOptions
{
    internal Dictionary<Type, string> Routes { get; } = [];

    internal List<Type> Subscriptions { get; } = [];

    internal Action<RebusConfigurer>? Customize { get; private set; }

    /// <summary>Messages of <typeparamref name="TMessage"/> sent with <c>bus.Send</c> go to <paramref name="queue"/>.</summary>
    public MiddtRebusOptions Map<TMessage>(string queue)
    {
        Routes[typeof(TMessage)] = queue;
        return this;
    }

    /// <summary>Subscribes this endpoint to published <typeparamref name="TEvent"/> messages at startup.</summary>
    public MiddtRebusOptions Subscribe<TEvent>()
    {
        Subscriptions.Add(typeof(TEvent));
        return this;
    }

    /// <summary>Escape hatch for any further Rebus configuration (sagas, timeouts, outbox, ...).</summary>
    public MiddtRebusOptions Configure(Action<RebusConfigurer> customize)
    {
        Customize += customize;
        return this;
    }
}
