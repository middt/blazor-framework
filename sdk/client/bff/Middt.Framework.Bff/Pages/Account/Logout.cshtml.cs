using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Oidc;
using Middt.Framework.Bff.Security;
using Middt.Framework.Common.Web;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Bff.Pages.Account;

/// <summary>
/// Logout requires a POST with antiforgery token, so third-party pages cannot log users out. A GET renders the form; it
/// submits itself only when the navigation comes from this app (<c>Sec-Fetch-Site: same-origin</c>, or a same-origin
/// <c>Origin</c>/<c>Referer</c> for browsers without Fetch Metadata), e.g. the logout buttons of the Angular/Blazor apps.
/// Links from other sites or typed URLs only show the confirmation button. Ends the server session, revokes the refresh
/// token at the API (OIDC mode: at the identity provider, then ends the provider session) and deletes the cookie.
/// </summary>
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class LogoutModel(
    CookieSignInService signInService,
    IOptions<SecuritySettings> securitySettings,
    MiddtBffOptions bffOptions,
    ILogger<LogoutModel> logger) : PageModel
{
    /// <summary>The form submits itself (same-origin navigation); otherwise the user confirms with the button.</summary>
    public bool AutoSubmit { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return LocalRedirect(securitySettings.Value.LoginPageURL);
        }

        AutoSubmit = IsSameOriginNavigation(Request);
        return Page();
    }

    /// <summary>True when the request was initiated by a page of this origin.</summary>
    public static bool IsSameOriginNavigation(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Fetch Metadata (all current browsers) cannot be set by pages: authoritative when present.
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0)
        {
            return string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase);
        }

        var source = request.Headers.Origin.ToString();
        if (source.Length == 0 || source == "null")
        {
            source = request.Headers.Referer.ToString();
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!bffOptions.Oidc.IsEnabled)
        {
            await signInService.SignOutAsync(HttpContext, cancellationToken);
            return LocalRedirect(securitySettings.Value.LoginPageURL);
        }

        // OIDC mode: revoke the refresh token at the provider, end the server session and the cookie, then end the
        // provider session (otherwise the next /Account/Login would sign the user in again without credentials).
        var idToken = await signInService.GetIdTokenAsync(HttpContext, cancellationToken);
        await signInService.SignOutAsync(HttpContext, cancellationToken);
        try
        {
            await BffOidc.SignOutAsync(HttpContext, idToken, LocalUrl.OrDefault(bffOptions.Oidc.SignedOutRedirectUri));
            return new EmptyResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // Provider unreachable / no end-session endpoint: the local session is gone already.
            LogEndSessionFailed(logger, ex);
            return LocalRedirect(LocalUrl.OrDefault(bffOptions.Oidc.SignedOutRedirectUri));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ending the identity provider session failed")]
    private static partial void LogEndSessionFailed(ILogger logger, Exception exception);
}
