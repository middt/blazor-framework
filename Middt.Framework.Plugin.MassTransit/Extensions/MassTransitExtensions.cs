using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middt.Framework.Plugin.MassTransit.Settings;

namespace Middt.Framework.Plugin.MassTransit;

public static class MassTransitExtensions
{
    /// <summary>
    /// Registers MassTransit on RabbitMQ using the <see cref="RabbitMqSettings.SectionName"/> configuration section.
    /// The bus is started/stopped by the MassTransit hosted service.
    /// </summary>
    public static IServiceCollection AddMiddtMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RabbitMqSettings>().Bind(configuration.GetSection(RabbitMqSettings.SectionName));
        return services.AddMiddtMassTransitCore(configure);
    }

    /// <summary>Registers MassTransit on RabbitMQ, configuring <see cref="RabbitMqSettings"/> in code.</summary>
    public static IServiceCollection AddMiddtMassTransit(
        this IServiceCollection services,
        Action<RabbitMqSettings> configureSettings,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configureSettings);

        services.AddOptions<RabbitMqSettings>().Configure(configureSettings);
        return services.AddMiddtMassTransitCore(configure);
    }

    private static IServiceCollection AddMiddtMassTransitCore(
        this IServiceCollection services,
        Action<IBusRegistrationConfigurator>? configure)
    {
        services.AddOptions<RabbitMqSettings>()
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.ConnectionString) || !string.IsNullOrWhiteSpace(s.URL),
                "RabbitMqSettings.ConnectionString or RabbitMqSettings.URL must be set.")
            .ValidateOnStart();

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                ConfigureHost(cfg, context.GetRequiredService<IOptions<RabbitMqSettings>>().Value);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static void ConfigureHost(IRabbitMqBusFactoryConfigurator cfg, RabbitMqSettings settings)
    {
        string? username;
        string? password;
        Uri hostAddress;

        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            hostAddress = new Uri(settings.ConnectionString);
            (username, password) = ParseUserInfo(hostAddress);
        }
        else
        {
            hostAddress = new Uri(settings.URL!);
            username = settings.Username;
            password = settings.Password;
        }

        cfg.Host(hostAddress, h =>
        {
            if (!string.IsNullOrEmpty(username))
            {
                h.Username(username);
            }

            if (!string.IsNullOrEmpty(password))
            {
                h.Password(password);
            }
        });
    }

    private static (string? Username, string? Password) ParseUserInfo(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return (null, null);
        }

        var parts = uri.UserInfo.Split(':', 2);
        return (
            Uri.UnescapeDataString(parts[0]),
            parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null);
    }
}
