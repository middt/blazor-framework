using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Middt.Framework.Common.Observability;

/// <summary>
/// Configures OpenTelemetry logging, metrics, and distributed tracing across Middt hosts (API, Workflow, Identity, BFFs)
/// using the standard OpenTelemetry OTLP exporter (<c>OTEL_EXPORTER_OTLP_*</c> or <c>Telemetry:Otlp</c> / <c>Telemetry:Exporters</c>),
/// enabling telemetry export to OpenObserve, OpenTelemetry Collector, Jaeger, Elastic APM, Grafana Tempo, Seq, or Aspire Dashboard
/// purely from configuration.
/// </summary>
public static class MiddtOpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry logs, metrics, and traces to <paramref name="builder"/>, binding <see cref="MiddtTelemetryOptions"/>
    /// from <c>Telemetry</c>, <c>Observability</c>, and standard <c>OTEL_*</c> configuration keys.
    /// </summary>
    public static TBuilder AddMiddtOpenTelemetry<TBuilder>(
        this TBuilder builder,
        Action<MiddtTelemetryOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new MiddtTelemetryOptions();
        builder.Configuration.GetSection(MiddtTelemetryOptions.AlternateSectionName).Bind(options);
        builder.Configuration.GetSection(MiddtTelemetryOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        var activeExporters = options.ResolveActiveExporters(builder.Configuration);
        var serviceName = !string.IsNullOrWhiteSpace(options.ServiceName)
            ? options.ServiceName
            : !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_SERVICE_NAME"])
                ? builder.Configuration["OTEL_SERVICE_NAME"]!
                : builder.Environment.ApplicationName;
        var serviceVersion = options.ServiceVersion
            ?? builder.Configuration["OTEL_SERVICE_VERSION"]
            ?? MiddtDiagnostics.Version;

        builder.Services.AddSingleton(options);

        if (options.LoggingEnabled)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.ParseStateValues = true;

                foreach (var exporter in activeExporters)
                {
                    var captured = exporter;
                    logging.AddOtlpExporter(o => ConfigureOtlpExporter(o, captured, "logs"));
                }
            });
        }

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
                ]));

        if (options.MetricsEnabled)
        {
            otel.WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Npgsql")
                    .AddMeter(builder.Environment.ApplicationName)
                    .AddMeter(MiddtDiagnostics.WorkflowSourceName)
                    .AddMeter(MiddtDiagnostics.ApiSourceName)
                    .AddMeter(MiddtDiagnostics.IdentitySourceName);

                foreach (var meter in options.AdditionalMeters)
                {
                    if (!string.IsNullOrWhiteSpace(meter))
                    {
                        metrics.AddMeter(meter);
                    }
                }

                for (var i = 0; i < activeExporters.Count; i++)
                {
                    var captured = activeExporters[i];
                    var name = string.Create(CultureInfo.InvariantCulture, $"otlp-{i}");
                    metrics.AddOtlpExporter(name, o => ConfigureOtlpExporter(o, captured, "metrics"));
                }
            });
        }

        if (options.TracingEnabled)
        {
            otel.WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource(MiddtDiagnostics.WorkflowSourceName)
                    .AddSource(MiddtDiagnostics.ApiSourceName)
                    .AddSource(MiddtDiagnostics.IdentitySourceName)
                    .AddSource("Npgsql")
                    .AddSource("RabbitMQ.Client.*")
                    .AddAspNetCoreInstrumentation(o =>
                        o.Filter = context => ShouldTraceHttpRequest(context.Request.Path.Value))
                    .AddHttpClientInstrumentation()
                    .AddProcessor(new MiddtRouteActivityProcessor());

                foreach (var source in options.AdditionalSources)
                {
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        tracing.AddSource(source);
                    }
                }

                for (var i = 0; i < activeExporters.Count; i++)
                {
                    var captured = activeExporters[i];
                    var name = string.Create(CultureInfo.InvariantCulture, $"otlp-{i}");
                    tracing.AddOtlpExporter(name, o => ConfigureOtlpExporter(o, captured, "traces"));
                }
            });
        }

        return builder;
    }

    internal static bool ShouldTraceHttpRequest(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/alive", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<string> staticExtensions = [".js", ".css", ".ico", ".woff", ".woff2", ".ttf", ".png", ".svg", ".jpg", ".jpeg", ".webp", ".map"];
        foreach (var ext in staticExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static void ConfigureOtlpExporter(OtlpExporterOptions exporter, MiddtOtlpOptions options, string signal)
    {
        exporter.Protocol = options.ResolveProtocol();
        exporter.Endpoint = options.BuildSignalEndpoint(signal);
        if (options.BuildHeaders() is { Length: > 0 } headers)
        {
            exporter.Headers = headers;
        }

        if (options.TimeoutMilliseconds is > 0 and var timeoutMs)
        {
            exporter.TimeoutMilliseconds = timeoutMs;
        }
    }
}
