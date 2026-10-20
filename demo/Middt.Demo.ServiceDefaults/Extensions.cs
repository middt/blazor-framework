using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Middt.Framework.Common.Observability;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Cross-cutting defaults shared by every sample service (Aspire convention):
/// OpenTelemetry (logs, metrics, traces → OpenObserve / OTLP), service discovery, HTTP resilience and a liveness check.
/// Health endpoints themselves are mapped by the Middt framework (<c>/health</c>, <c>/alive</c>).
/// </summary>
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder, bool enableOpenTelemetry = false)
        where TBuilder : IHostApplicationBuilder
    {
        if (enableOpenTelemetry)
        {
            builder.ConfigureOpenTelemetry();
        }

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder =>
        builder.AddMiddtOpenTelemetry();

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }
}
