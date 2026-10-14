namespace Middt.Framework.Api;

/// <summary>Options for <see cref="DependencyInjection.MiddtApiExtensions.AddMiddtApi"/>.</summary>
public sealed class MiddtApiOptions
{
    /// <summary>Title used in the generated OpenAPI documents.</summary>
    public string Title { get; set; } = "Middt API";

    /// <summary>API versions that get an OpenAPI document (<c>v1</c>, <c>v2</c>, ...).</summary>
    public IList<int> ApiVersions { get; } = [1];

    /// <summary>Maps <c>/openapi/{version}.json</c> and the Scalar UI at <c>/scalar</c>.</summary>
    public bool EnableOpenApi { get; set; } = true;

    /// <summary>
    /// Outside Development only operations marked with <see cref="OpenApi.SwaggerTagAttribute"/>(<c>true</c>) are documented.
    /// </summary>
    public bool HideUntaggedOperationsOutsideDevelopment { get; set; } = true;

    /// <summary>Physical folder (relative to the content root) served under <see cref="StaticFilesRequestPath"/>. Ignored when missing.</summary>
    public string? StaticFilesFolder { get; set; } = "Files";

    public string StaticFilesRequestPath { get; set; } = "/Files";

    /// <summary>Requests under this path may pass the JWT as <c>access_token</c> query parameter (SignalR WebSockets).</summary>
    public string HubPathPrefix { get; set; } = "/hubs";

    /// <summary>Permitted token requests per client IP per minute (login/refresh brute-force protection).</summary>
    public int TokenRequestsPerMinute { get; set; } = 10;
}
