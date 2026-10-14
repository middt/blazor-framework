namespace Middt.Framework.Common.Web;

/// <summary>HTTP response headers applied by <see cref="SecurityHeadersExtensions.UseMiddtSecurityHeaders"/>.</summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>Content-Security-Policy value; <c>null</c> disables the header.</summary>
    public string? ContentSecurityPolicy { get; set; }

    /// <summary>Request paths (prefix match) that do not receive the CSP header, e.g. a third-party docs UI.</summary>
    public IList<string> ContentSecurityPolicyExcludedPaths { get; } = [];

    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    public string FrameOptions { get; set; } = "DENY";

    /// <summary>Policy suited to JSON APIs: nothing may be loaded or framed.</summary>
    public static string ApiContentSecurityPolicy => "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Policy for single page applications (Angular, React) served by a BFF: no inline scripts, same-origin API/WebSocket.</summary>
    public static string SpaContentSecurityPolicy =>
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    /// <summary>
    /// Policy suited to Blazor Server + Radzen/Syncfusion: same-origin scripts only (no inline/eval),
    /// inline style attributes (required by the component libraries) and same-origin WebSockets.
    /// </summary>
    public static string BlazorServerContentSecurityPolicy =>
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
}
