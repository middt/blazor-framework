using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Middt.Identity;

/// <summary>Names shared by the protocol, the journeys and the UI.</summary>
public static class MiddtIdentityDefaults
{
    /// <summary>Authentication scheme of the identity provider's own session cookie.</summary>
    public const string CookieScheme = "Middt.Identity";

    /// <summary>Session cookie name (differs from the BFF cookies – browsers share cookies across ports).</summary>
    public const string CookieName = "middt.idp";

    /// <summary>Cookie binding the browser to its running journey (<c>{id}.{secret}</c>).</summary>
    public const string JourneyCookieName = "middt.idp.journey";

    /// <summary>Role required for the administration UI.</summary>
    public const string AdminRole = "idp-admin";

    public const string LoginPath = "/account/login";
    public const string LogoutPath = "/account/logout";
    public const string RegisterPath = "/account/register";
    public const string ForgotPasswordPath = "/account/forgot-password";
    public const string AccessDeniedPath = "/account/denied";
    public const string DevicePath = "/account/device";

    /// <summary>Page that renders journey <c>{id}</c>.</summary>
    public static string JourneyPath(Guid id) => $"/account/journey/{id.ToString("D", CultureInfo.InvariantCulture)}";

    /// <summary>Endpoint that consumes a succeeded journey, signs in and redirects (mapped by <c>MapMiddtIdentity</c>).</summary>
    public static string JourneyCompletePath(Guid id) => JourneyPath(id) + "/complete";
}

/// <summary>Reads and writes the journey binding cookie (HttpOnly, SameSite=Lax, Secure on HTTPS, path <c>/account</c>).</summary>
public static class AuthJourneyCookie
{
    public static void Write(HttpContext context, AuthJourneyHandle handle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handle);
        context.Response.Cookies.Append(MiddtIdentityDefaults.JourneyCookieName, $"{handle.Id:N}.{handle.Binding}", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/account",
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(30),
        });
    }

    /// <summary>The binding secret for journey <paramref name="id"/>, or <c>null</c> when this browser did not start it.</summary>
    public static string? Read(HttpContext context, Guid id)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Cookies[MiddtIdentityDefaults.JourneyCookieName] is not { } value)
        {
            return null;
        }

        var dot = value.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && Guid.TryParseExact(value[..dot], "N", out var cookieId) && cookieId == id && dot < value.Length - 1
            ? value[(dot + 1)..]
            : null;
    }

    public static void Delete(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(MiddtIdentityDefaults.JourneyCookieName, new CookieOptions { Path = "/account" });
    }
}
