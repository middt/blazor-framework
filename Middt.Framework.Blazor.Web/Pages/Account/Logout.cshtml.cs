using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Blazor.Web.Pages.Account;

/// <summary>
/// Logout requires a POST with antiforgery token (a GET only renders a self-submitting form), so third-party pages
/// cannot log users out. Ends the server session, revokes the refresh token at the API and deletes the cookie.
/// </summary>
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogoutModel(CookieSignInService signInService, IOptions<SecuritySettings> securitySettings) : PageModel
{
    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? Page() : LocalRedirect(securitySettings.Value.LoginPageURL);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await signInService.SignOutAsync(HttpContext, cancellationToken);
        return LocalRedirect(securitySettings.Value.LoginPageURL);
    }
}
