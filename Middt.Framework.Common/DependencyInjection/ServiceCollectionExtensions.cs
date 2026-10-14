using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Email;
using Middt.Framework.Common.Http;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Common.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers services declared in the <c>DependencySettings</c> configuration section
    /// (configuration driven plug-ins). Unknown types fail fast at startup.
    /// </summary>
    public static IServiceCollection AddServicesFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(DependencySettings.SectionName).Get<DependencySettings>();
        foreach (var dependency in settings?.DependencyModelList ?? [])
        {
            var serviceType = Type.GetType(dependency.ServiceType, throwOnError: true)!;
            var implementationType = Type.GetType(dependency.ImplementationType, throwOnError: true)!;
            services.Add(new ServiceDescriptor(serviceType, implementationType, dependency.Lifetime));
        }

        return services;
    }

    /// <summary>
    /// Binds <see cref="ApiSettings"/> and registers the token client used by every Middt API client.
    /// </summary>
    public static IHttpClientBuilder AddMiddtApiClients(this IServiceCollection services, IConfiguration configuration) =>
        services.AddMiddtApiClients<BaseTokenService>(configuration);

    /// <inheritdoc cref="AddMiddtApiClients(IServiceCollection, IConfiguration)"/>
    public static IHttpClientBuilder AddMiddtApiClients<TTokenService>(this IServiceCollection services, IConfiguration configuration)
        where TTokenService : BaseTokenService
    {
        services.AddOptions<ApiSettings>()
            .Bind(configuration.GetSection(ApiSettings.SectionName))
            .Validate(s => Uri.TryCreate(s.URL, UriKind.Absolute, out _), "ApiSettings:URL must be an absolute URI.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<ForwardedClientIpHandler>();

        return services.AddMiddtApiClient<ITokenService, TTokenService>()
            .AddHttpMessageHandler<ForwardedClientIpHandler>();
    }

    /// <summary>
    /// Registers a typed API client with <see cref="IHttpClientFactory"/>, its base address from
    /// <see cref="ApiSettings"/> and the standard resilience pipeline (retry, circuit breaker, timeouts).
    /// </summary>
    public static IHttpClientBuilder AddMiddtApiClient<TClient>(this IServiceCollection services)
        where TClient : class =>
        services.AddHttpClient<TClient>(ConfigureClient).AddMiddtResilience();

    /// <inheritdoc cref="AddMiddtApiClient{TClient}(IServiceCollection)"/>
    public static IHttpClientBuilder AddMiddtApiClient<TService, TClient>(this IServiceCollection services)
        where TService : class
        where TClient : class, TService =>
        services.AddHttpClient<TService, TClient>(ConfigureClient).AddMiddtResilience();

    public static IServiceCollection AddMiddtEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiSettings>>().Value;
        client.BaseAddress = new Uri(settings.URL.TrimEnd('/') + "/");
    }

    private static IHttpClientBuilder AddMiddtResilience(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler();
        return builder;
    }
}
