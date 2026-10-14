using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Middt.Identity.Ui.Components.Account;

/// <summary>The identity provider's own session (cookie scheme <see cref="MiddtIdentityDefaults.CookieScheme"/>).</summary>
public static class AccountSession
{
    /// <summary>
    /// The signed-in user of the IdP cookie, independent of the host's default scheme; an anonymous principal when
    /// there is no session.
    /// </summary>
    public static async Task<ClaimsPrincipal> GetPrincipalAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == MiddtIdentityDefaults.CookieScheme))
        {
            return context.User;
        }

        var result = await context.AuthenticateAsync(MiddtIdentityDefaults.CookieScheme);
        return result.Succeeded && result.Principal is { } principal ? principal : new ClaimsPrincipal(new ClaimsIdentity());
    }
}
