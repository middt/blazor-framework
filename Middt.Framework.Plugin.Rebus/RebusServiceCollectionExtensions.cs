using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using Rebus.Transport.InMem;

namespace Middt.Framework.Plugin.Rebus;

public static class RebusServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Rebus bus (<see cref="global::Rebus.Bus.IBus"/>) bound to the <c>RebusSettings</c> configuration section:
    /// RabbitMQ transport (native publish/subscribe) or the in-memory transport, System.Text.Json serialization,
    /// retries with an error queue, type based send routes and startup subscriptions. Register handlers with
    /// <see cref="AddMiddtRebusHandlersFromAssemblyOf{T}"/> or Rebus' <c>AddRebusHandler&lt;T&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddMiddtRebus(this IServiceCollection services, IConfiguration configuration, Action<MiddtRebusOptions>? configure = null) =>
        services.AddMiddtRebus(settings => configuration.GetSection(RebusSettings.SectionName).Bind(settings), configure);

    /// <inheritdoc cref="AddMiddtRebus(IServiceCollection, IConfiguration, Action{MiddtRebusOptions}?)"/>
    public static IServiceCollection AddMiddtRebus(this IServiceCollection services, Action<RebusSettings> configureSettings, Action<MiddtRebusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configureSettings);

        var options = new MiddtRebusOptions();
        configure?.Invoke(options);

        services.AddOptions<RebusSettings>()
            .Configure(configureSettings)
            .ValidateDataAnnotations()
            .Validate(s => s.UseInMemoryTransport || !string.IsNullOrWhiteSpace(s.ConnectionString),
                "RebusSettings:ConnectionString is required unless UseInMemoryTransport is enabled.")
            .ValidateOnStart();

        services.TryAddSingleton<InMemNetwork>();

        services.AddRebus(
            (configurer, provider) =>
            {
                var settings = provider.GetRequiredService<IOptions<RebusSettings>>().Value;

                configurer
                    .Transport(t =>
                    {
                        if (settings.UseInMemoryTransport)
                        {
                            t.UseInMemoryTransport(provider.GetRequiredService<InMemNetwork>(), settings.InputQueueName);
                        }
                        else
                        {
                            t.UseRabbitMq(settings.ConnectionString, settings.InputQueueName);
                        }
                    })
                    // Plain JSON contracts: no type-name driven polymorphic deserialization of incoming payloads.
                    .Serialization(s => s.UseSystemTextJson())
                    .Routing(r =>
                    {
                        var routing = r.TypeBased();
                        foreach (var (type, queue) in options.Routes)
                        {
                            routing.Map(type, queue);
                        }

                        foreach (var (typeName, queue) in settings.Routes)
                        {
                            routing.Map(Type.GetType(typeName, throwOnError: true)!, queue);
                        }
                    })
                    .Options(o =>
                    {
                        o.RetryStrategy(errorQueueName: settings.ErrorQueueName, maxDeliveryAttempts: settings.MaxDeliveryAttempts);
                        o.SetNumberOfWorkers(settings.NumberOfWorkers);
                        o.SetMaxParallelism(settings.MaxParallelism);
                    });

                options.Customize?.Invoke(configurer);
                return configurer;
            },
            onCreated: async bus =>
            {
                foreach (var eventType in options.Subscriptions)
                {
                    await bus.Subscribe(eventType);
                }
            });

        return services;
    }

    /// <summary>Registers every <c>IHandleMessages&lt;T&gt;</c> implementation of <typeparamref name="T"/>'s assembly.</summary>
    public static IServiceCollection AddMiddtRebusHandlersFromAssemblyOf<T>(this IServiceCollection services)
    {
        services.AutoRegisterHandlersFromAssemblyOf<T>();
        return services;
    }
}
