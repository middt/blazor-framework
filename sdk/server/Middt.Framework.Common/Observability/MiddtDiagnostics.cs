using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Middt.Framework.Common.Observability;

/// <summary>
/// Shared OpenTelemetry <see cref="ActivitySource"/> and <see cref="Meter"/> names across Middt server packages.
/// </summary>
public static class MiddtDiagnostics
{
    /// <summary>Version of the Middt observability instrumentation.</summary>
    public const string Version = "1.0.0";

    /// <summary>ActivitySource and Meter name emitted by <c>Middt.Framework.Workflow</c>.</summary>
    public const string WorkflowSourceName = "Middt.Framework.Workflow";

    /// <summary>ActivitySource and Meter name emitted by <c>Middt.Framework.Api</c>.</summary>
    public const string ApiSourceName = "Middt.Framework.Api";

    /// <summary>ActivitySource and Meter name emitted by <c>Middt.Identity</c>.</summary>
    public const string IdentitySourceName = "Middt.Identity";

    /// <summary>ActivitySource for API operations.</summary>
    public static readonly ActivitySource ApiActivitySource = new(ApiSourceName, Version);

    /// <summary>Meter for API operations.</summary>
    public static readonly Meter ApiMeter = new(ApiSourceName, Version);

    /// <summary>ActivitySource for Identity operations.</summary>
    public static readonly ActivitySource IdentityActivitySource = new(IdentitySourceName, Version);

    /// <summary>Meter for Identity operations.</summary>
    public static readonly Meter IdentityMeter = new(IdentitySourceName, Version);
}
