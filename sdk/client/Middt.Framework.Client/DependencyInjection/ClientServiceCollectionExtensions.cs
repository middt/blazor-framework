using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Client.Security;

namespace Middt.Framework.Client.DependencyInjection;

/// <summary>Registration of the .NET client SDK (typed API clients on <see cref="IHttpClientFactory"/> + resilience).</summary>
public static class ClientServiceCollectionExtensions
{
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

        // Login/refresh/revoke are never retried: a retried login consumes rate limiter permits (and a 429 answer would be
        // retried into more 429s); refresh tokens are single use, so a resent refresh is treated as reuse by the API.
        return services.AddHttpClient<ITokenService, TTokenService>(ConfigureClient)
            .AddMiddtResilience(ResilienceProfile.NoRetry)
            .AddHttpMessageHandler<ForwardedClientIpHandler>();
    }

    /// <summary>Attempt timeout of <see cref="WorkflowApiClient"/>: a transition runs its tasks synchronously (up to ~30 s).</summary>
    public static readonly TimeSpan WorkflowAttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Registers a typed API client with <see cref="IHttpClientFactory"/>, its base address from
    /// <see cref="ApiSettings"/> and the standard resilience pipeline (circuit breaker, timeouts, retry). Retries are
    /// limited to safe methods (GET/HEAD/OPTIONS/TRACE) and transient failures (5xx, 408, network errors, attempt
    /// timeouts): POST/PUT/DELETE are never resent and 429 is never retried. <see cref="WorkflowApiClient"/> (and
    /// derived clients) get a <see cref="WorkflowAttemptTimeout"/> attempt timeout.
    /// </summary>
    public static IHttpClientBuilder AddMiddtApiClient<TClient>(this IServiceCollection services)
        where TClient : class =>
        services.AddHttpClient<TClient>(ConfigureClient).AddMiddtResilience(ProfileOf(typeof(TClient)));

    /// <inheritdoc cref="AddMiddtApiClient{TClient}(IServiceCollection)"/>
    public static IHttpClientBuilder AddMiddtApiClient<TService, TClient>(this IServiceCollection services)
        where TService : class
        where TClient : class, TService =>
        services.AddHttpClient<TService, TClient>(ConfigureClient).AddMiddtResilience(ProfileOf(typeof(TClient)));

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiSettings>>().Value;
        client.BaseAddress = new Uri(settings.URL.TrimEnd('/') + "/");
    }

    private static ResilienceProfile ProfileOf(Type client) =>
        typeof(ITokenService).IsAssignableFrom(client) ? ResilienceProfile.NoRetry
        : typeof(WorkflowApiClient).IsAssignableFrom(client) ? ResilienceProfile.LongRunning
        : ResilienceProfile.Standard;

    private static IHttpClientBuilder AddMiddtResilience(this IHttpClientBuilder builder, ResilienceProfile profile)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            // Transient failures only, and never 429: retrying a rate limited call just burns more permits.
            options.Retry.ShouldHandle = args => ValueTask.FromResult(
                profile != ResilienceProfile.NoRetry
                && HttpClientResiliencePredicates.IsTransient(args.Outcome)
                && args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);
            // POST/PUT/PATCH/DELETE may have been executed by the server (e.g. a slow workflow transition): never resend.
            options.Retry.DisableForUnsafeHttpMethods();

            if (profile == ResilienceProfile.LongRunning)
            {
                options.AttemptTimeout.Timeout = WorkflowAttemptTimeout;
                options.TotalRequestTimeout.Timeout = WorkflowAttemptTimeout * 2;
                // The circuit breaker's sampling duration must be at least twice the attempt timeout.
                options.CircuitBreaker.SamplingDuration = WorkflowAttemptTimeout * 2;
            }
        });
        return builder;
    }

    private enum ResilienceProfile
    {
        Standard,
        LongRunning,
        NoRetry,
    }
}
