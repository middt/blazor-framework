using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Configuration;

namespace Middt.Framework.Bff.Pages.Account;

/// <summary>
/// Target of the cookie's access denied redirect (signed in, but a required role is missing). A separate page instead of
/// the login page: with an identity provider the login page signs in silently and would loop back to the forbidden page.
/// </summary>
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccessDeniedModel(IOptions<SecuritySettings> securitySettings) : PageModel
{
    public string LogoutUrl => securitySettings.Value.LogoutPageURL;

    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
