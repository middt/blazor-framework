using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace Middt.Framework.Common.Observability;

/// <summary>
/// Vendor-neutral OpenTelemetry OTLP exporter configuration.
/// Can target any OTLP-compatible backend (OpenObserve, OpenTelemetry Collector, Jaeger, Grafana Tempo/Alloy,
/// Elastic APM, Seq, Aspire Dashboard, etc.) purely via configuration.
/// </summary>
public sealed class MiddtOtlpOptions
{
    /// <summary>
    /// OTLP base endpoint URI (e.g., <c>http://otel-collector:4318</c>, <c>http://openobserve:5080/api/default</c>,
    /// <c>http://jaeger:4318</c>, or <c>http://otel-collector:4317</c>).
    /// For <c>http/protobuf</c>, <c>/v1/traces</c>, <c>/v1/metrics</c>, and <c>/v1/logs</c> are appended automatically
    /// when not already present.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OTLP transport protocol: <c>http/protobuf</c> (default) or <c>grpc</c>.
    /// </summary>
    public string Protocol { get; set; } = "http/protobuf";

    /// <summary>
    /// Optional comma-separated OTLP headers (e.g., <c>Authorization=Basic ...,stream-name=default</c>).
    /// Matches the standard <c>OTEL_EXPORTER_OTLP_HEADERS</c> format.
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>
    /// Optional key-value OTLP headers that can be bound from structured JSON configuration
    /// and merged with <see cref="Headers"/>.
    /// </summary>
    public IDictionary<string, string> HeaderValues { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional export timeout in milliseconds.</summary>
    public int? TimeoutMilliseconds { get; set; }

    /// <summary>Returns <see langword="true"/> when <see cref="Endpoint"/> is configured.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// Parses <see cref="Protocol"/> (<c>http/protobuf</c>, <c>http</c>, <c>HttpProtobuf</c>, or <c>grpc</c>)
    /// into an <see cref="OtlpExportProtocol"/> value.
    /// </summary>
    public OtlpExportProtocol ResolveProtocol()
    {
        if (string.IsNullOrWhiteSpace(Protocol))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        var normalized = Protocol.Trim();
        if (normalized.Equals("grpc", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.Grpc;
        }

        if (normalized.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("http", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        throw new InvalidOperationException(
            $"Unsupported OTLP protocol '{Protocol}'. Expected 'http/protobuf' or 'grpc'.");
    }

    /// <summary>
    /// Builds the target OTLP URI for <paramref name="signal"/> (<c>traces</c>, <c>metrics</c>, or <c>logs</c>).
    /// For <see cref="OtlpExportProtocol.HttpProtobuf"/>, appends <c>/v1/{signal}</c> when not already present.
    /// </summary>
    public Uri BuildSignalEndpoint(string signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            throw new InvalidOperationException("OTLP Endpoint is not configured.");
        }

        var trimmed = Endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"OTLP Endpoint '{Endpoint}' must be an absolute HTTP or HTTPS URI.");
        }

        if (ResolveProtocol() == OtlpExportProtocol.Grpc)
        {
            return baseUri;
        }

        var normalizedSignal = signal.Trim().TrimStart('/');
        if (trimmed.EndsWith("/v1/traces", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/v1/metrics", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/v1/logs", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = trimmed.LastIndexOf('/');
            return new Uri($"{trimmed[..lastSlash]}/{normalizedSignal}");
        }

        return new Uri($"{trimmed}/v1/{normalizedSignal}");
    }

    /// <summary>
    /// Builds the combined comma-separated OTLP headers string from <see cref="Headers"/> and <see cref="HeaderValues"/>.
    /// </summary>
    public string? BuildHeaders()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Headers))
        {
            foreach (var segment in Headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    parts.Add(segment);
                }
            }
        }

        foreach (var (key, value) in HeaderValues)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                parts.Add($"{key.Trim()}={value.Trim()}");
            }
        }

        return parts.Count > 0 ? string.Join(",", parts) : null;
    }
}

/// <summary>
/// Options for <see cref="MiddtOpenTelemetryExtensions.AddMiddtOpenTelemetry"/>.
/// Bound from <c>Telemetry</c>, <c>Observability</c>, and standard <c>OTEL_*</c> configuration keys.
/// </summary>
public sealed class MiddtTelemetryOptions
{
    /// <summary>Primary configuration section name.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>Alternative configuration section name.</summary>
    public const string AlternateSectionName = "Observability";

    /// <summary>Optional logical service name override (defaults to <c>OTEL_SERVICE_NAME</c> or <c>IHostEnvironment.ApplicationName</c>).</summary>
    public string? ServiceName { get; set; }

    /// <summary>Optional logical service version override (defaults to <c>OTEL_SERVICE_VERSION</c> or framework assembly version).</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Whether distributed tracing is enabled (defaults to <see langword="true"/>).</summary>
    public bool TracingEnabled { get; set; } = true;

    /// <summary>Whether metrics collection is enabled (defaults to <see langword="true"/>).</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>Whether OpenTelemetry log export is enabled (defaults to <see langword="true"/>).</summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>Primary OTLP exporter configuration (<c>Telemetry:Otlp</c> or <c>OTEL_EXPORTER_OTLP_*</c>).</summary>
    public MiddtOtlpOptions Otlp { get; } = new();

    /// <summary>
    /// Additional OTLP exporters (<c>Telemetry:Exporters</c>) to fan out telemetry to multiple backends simultaneously
    /// (e.g. OpenObserve + Jaeger / Elastic APM / Aspire Dashboard) directly from configuration.
    /// </summary>
    public IList<MiddtOtlpOptions> Exporters { get; } = [];

    /// <summary>Additional <see cref="System.Diagnostics.ActivitySource"/> names to subscribe to.</summary>
    public IList<string> AdditionalSources { get; } = [];

    /// <summary>Additional <see cref="System.Diagnostics.Metrics.Meter"/> names to subscribe to.</summary>
    public IList<string> AdditionalMeters { get; } = [];

    /// <summary>
    /// Resolves all active OTLP exporter configurations from <see cref="Otlp"/>, <see cref="Exporters"/>,
    /// and standard <c>OTEL_EXPORTER_OTLP_*</c> environment/configuration keys.
    /// </summary>
    public IReadOnlyList<MiddtOtlpOptions> ResolveActiveExporters(IConfiguration? configuration = null)
    {
        var active = new List<MiddtOtlpOptions>();
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(MiddtOtlpOptions candidate)
        {
            if (!candidate.IsEnabled)
            {
                return;
            }

            var key = $"{candidate.ResolveProtocol()}:{candidate.Endpoint!.Trim().TrimEnd('/')}";
            if (seenEndpoints.Add(key))
            {
                active.Add(candidate);
            }
        }

        TryAdd(Otlp);

        foreach (var exporter in Exporters)
        {
            TryAdd(exporter);
        }

        var envEndpoint = configuration?["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(envEndpoint))
        {
            var envExporter = new MiddtOtlpOptions
            {
                Endpoint = envEndpoint,
                Protocol = configuration?["OTEL_EXPORTER_OTLP_PROTOCOL"] is { Length: > 0 } proto
                    ? proto
                    : GuessDefaultProtocol(envEndpoint),
                Headers = configuration?["OTEL_EXPORTER_OTLP_HEADERS"],
                TimeoutMilliseconds = int.TryParse(configuration?["OTEL_EXPORTER_OTLP_TIMEOUT"], out var timeout)
                    ? timeout
                    : null,
            };
            TryAdd(envExporter);
        }

        return active;
    }

    private static string GuessDefaultProtocol(string endpoint)
    {
        // Standard OTLP gRPC port is 4317 (and Aspire Dashboard uses gRPC on arbitrary local ports unless specified);
        // when a path like /api/default or port 4318/5080/5180 is present, default to http/protobuf.
        if (Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
        {
            if (uri.Port == 4317)
            {
                return "grpc";
            }

            if (uri.Port is 4318 or 5080 or 5180 ||
                (uri.AbsolutePath.Length > 1 && !uri.AbsolutePath.Equals("/", StringComparison.Ordinal)))
            {
                return "http/protobuf";
            }
        }

        return "grpc";
    }
}
