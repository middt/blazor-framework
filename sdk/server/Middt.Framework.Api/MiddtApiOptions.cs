namespace Middt.Framework.Api;

/// <summary>Options for <see cref="DependencyInjection.MiddtApiExtensions.AddMiddtApi"/>.</summary>
public sealed class MiddtApiOptions
{
    /// <summary>Title used in the generated OpenAPI documents.</summary>
    public string Title { get; set; } = "Middt API";

    /// <summary>API versions that get an OpenAPI document (<c>v1</c>, <c>v2</c>, ...).</summary>
    public IList<int> ApiVersions { get; } = [1];

    /// <summary>
    /// Maps <c>/openapi/{version}.json</c> and the Scalar UI at <c>/scalar</c>. <c>null</c> (default) = Development only, so the
    /// documented API surface is not published to anonymous callers in production; set <c>true</c> to expose it everywhere.
    /// </summary>
    public bool? EnableOpenApi { get; set; }

    /// <summary>
    /// Outside Development only operations marked with <see cref="OpenApi.SwaggerTagAttribute"/>(<c>true</c>) are documented.
    /// </summary>
    public bool HideUntaggedOperationsOutsideDevelopment { get; set; } = true;

    /// <summary>Physical folder (relative to the content root) served under <see cref="StaticFilesRequestPath"/>. Ignored when missing.</summary>
    public string? StaticFilesFolder { get; set; } = "Files";

    public string StaticFilesRequestPath { get; set; } = "/Files";

    /// <summary>Requests under this path may pass the JWT as <c>access_token</c> query parameter (SignalR WebSockets).</summary>
    public string HubPathPrefix { get; set; } = "/hubs";

    /// <summary>Largest page a client may request from <c>getitems</c>; larger or non-positive sizes are clamped to it.</summary>
    public int MaxPageSize { get; set; } = 500;

    /// <summary>Most rows <c>getall</c> returns (lookups); use paged <c>getitems</c> for large tables.</summary>
    public int MaxGetAllItems { get; set; } = 1000;

    /// <summary>
    /// Brotli/Gzip compression (fastest level) of JSON and text responses, also over HTTPS. Turn off when a reverse proxy
    /// compresses already.
    /// </summary>
    public bool EnableResponseCompression { get; set; } = true;

    /// <summary>Permitted token requests per client IP per minute (login/refresh brute-force protection).</summary>
    public int TokenRequestsPerMinute { get; set; } = 10;

    /// <summary>
    /// OpenID Connect issuer (public URL) whose access tokens the API accepts. When set (here or in configuration
    /// <c>Authentication:Authority</c>), JWT bearer validation uses the authority's discovery document and signing keys
    /// instead of the symmetric <c>JwtSettings</c> key, and <c>JwtSettings</c> becomes optional.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Expected <c>aud</c> of access tokens in authority mode (configuration <c>Authentication:Audience</c>, default <c>middt-api</c>).</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Internal URL the API uses to reach the authority (configuration <c>Authentication:BackchannelAuthority</c>, e.g.
    /// <c>http://middt-identity:8080/</c> inside a container network). The issuer stays <see cref="Authority"/>.
    /// </summary>
    public string? BackchannelAuthority { get; set; }

    /// <summary>
    /// Requires HTTPS for discovery and key retrieval (configuration <c>Authentication:RequireHttpsMetadata</c>);
    /// <c>null</c> = required outside Development.
    /// </summary>
    public bool? RequireHttpsMetadata { get; set; }
}
