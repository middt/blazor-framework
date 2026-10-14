using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Oidc;
using Middt.Framework.Bff.Security;
using Middt.Framework.Common.Web;

namespace Middt.Framework.Bff.Pages.Account;

/// <summary>
/// Server rendered login (HTTP form post with antiforgery token): the Blazor circuit cannot set cookies.
/// Override by adding a page with the same path to the application.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(MiddtBffExtensions.LoginRateLimitPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginModel(CookieSignInService signInService, LoginAttemptLimiter attemptLimiter, MiddtBffOptions bffOptions) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(LocalUrl.OrDefault(ReturnUrl));
        }

        // OIDC mode: sign-in happens at the identity provider; it returns to the (local only) return URL.
        return bffOptions.Oidc.IsEnabled
            ? Challenge(new AuthenticationProperties { RedirectUri = LocalUrl.OrDefault(ReturnUrl) }, BffOidc.SchemeName)
            : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // No password form in OIDC mode: credentials are only ever entered at the identity provider.
        if (bffOptions.Oidc.IsEnabled)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            ErrorMessage = "Kullanıcı adı ve şifre zorunludur.";
            return Page();
        }

        // Chained after the per-IP policy ([EnableRateLimiting]): failed attempts per account across all IPs.
        if (!attemptLimiter.IsAllowed(Input.Username))
        {
            ErrorMessage = "Çok fazla giriş denemesi. Lütfen biraz sonra tekrar deneyin.";
            Input.Password = string.Empty;
            var page = Page();
            page.StatusCode = StatusCodes.Status429TooManyRequests;
            return page;
        }

        var result = await signInService.SignInAsync(HttpContext, Input.Username, Input.Password, cancellationToken);
        if (!result.IsSuccess)
        {
            attemptLimiter.RecordFailure(Input.Username);
            ErrorMessage = result.MessageList.FirstOrDefault() ?? "Giriş başarısız.";
            Input.Password = string.Empty;
            return Page();
        }

        // Only local return URLs: prevents open redirects (e.g. returnUrl=//evil.example).
        return LocalRedirect(LocalUrl.OrDefault(ReturnUrl));
    }

    public sealed class InputModel
    {
        [Required, MaxLength(256)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(256), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
