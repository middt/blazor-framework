using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;

namespace Middt.Framework.Plugin.RabbitMQ;

public static class RabbitMqExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IConnectionFactory"/> for an AMQP URI
    /// (e.g. <c>amqp://user:pass@host:5672/vhost</c>) used by the producer/consumer base classes.
    /// </summary>
    public static IServiceCollection AddMiddtRabbitMq(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var uri = new Uri(connectionString);
        services.TryAddSingleton<IConnectionFactory>(_ => new ConnectionFactory { Uri = uri });
        return services;
    }
}
