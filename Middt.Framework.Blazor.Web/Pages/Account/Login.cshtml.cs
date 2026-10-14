using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Middt.Framework.Blazor.Web.DependencyInjection;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Common.Web;

namespace Middt.Framework.Blazor.Web.Pages.Account;

/// <summary>
/// Server rendered login (HTTP form post with antiforgery token): the Blazor circuit cannot set cookies.
/// Override by adding a page with the same path to the application.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(MiddtBlazorExtensions.LoginRateLimitPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginModel(CookieSignInService signInService) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(LocalUrl.OrDefault(ReturnUrl)) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Kullanıcı adı ve şifre zorunludur.";
            return Page();
        }

        var result = await signInService.SignInAsync(HttpContext, Input.Username, Input.Password, cancellationToken);
        if (!result.IsSuccess)
        {
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
