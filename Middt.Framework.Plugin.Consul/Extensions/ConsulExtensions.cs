using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Middt.Framework.Plugin.Consul.Settings;

namespace Middt.Framework.Plugin.Consul.Extensions;

public static class ConsulExtensions
{
    /// <summary>Registers only the <see cref="IConsulClient"/> for the given settings.</summary>
    public static IServiceCollection AddConsulConfig(this IServiceCollection services, ConsulSettings consulSettings)
    {
        ArgumentNullException.ThrowIfNull(consulSettings);

        var address = new Uri(consulSettings.ConsoleHost);
        services.TryAddSingleton<IConsulClient>(_ => new ConsulClient(c => c.Address = address));
        return services;
    }

    /// <summary>
    /// Binds <see cref="ConsulSettings.SectionName"/>, registers the <see cref="IConsulClient"/> and the
    /// <see cref="ConsulRegistrationService"/> that registers/deregisters the service with Consul.
    /// </summary>
    public static IServiceCollection AddMiddtConsul(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ConsulSettings>()
            .Bind(configuration.GetSection(ConsulSettings.SectionName))
            .Validate(s => Uri.IsWellFormedUriString(s.ConsoleHost, UriKind.Absolute), "ConsulSettings.ConsoleHost must be an absolute URI.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ServiceName), "ConsulSettings.ServiceName must be set.")
            .ValidateOnStart();

        services.TryAddSingleton<IConsulClient>(sp =>
        {
            var address = new Uri(sp.GetRequiredService<IOptions<ConsulSettings>>().Value.ConsoleHost);
            return new ConsulClient(c => c.Address = address);
        });

        services.AddHostedService<ConsulRegistrationService>();
        return services;
    }
}
