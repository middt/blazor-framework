using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Middt.Framework.Common.Web;
using Middt.Identity.Ui.Components;
using Middt.Identity.Ui.Journeys;
using Radzen;

namespace Middt.Identity.Ui;

/// <summary>
/// Registration of the identity UI. Host wiring (Blazor Web App, .NET 10):
/// <code>
/// builder.Services.AddRazorComponents().AddInteractiveServerComponents();
/// builder.Services.AddMiddtIdentityUi();
/// …
/// app.UseMiddtIdentitySecurityHeaders();
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.UseAntiforgery();
/// app.MapStaticAssets();
/// app.MapMiddtIdentityUi();   // MapRazorComponents&lt;MiddtIdentityApp&gt;().AddInteractiveServerRenderMode()
/// </code>
/// </summary>
public static class MiddtIdentityUiServiceCollectionExtensions
{
    /// <summary>
    /// Services of the account and administration pages: Radzen, cascading authentication state, journey views and the
    /// authorization policies <see cref="MiddtIdentityUi.AdminPolicy"/> / <see cref="MiddtIdentityUi.AccountPolicy"/>.
    /// </summary>
    public static IServiceCollection AddMiddtIdentityUi(this IServiceCollection services, Action<MiddtIdentityUiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<MiddtIdentityUiOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>().Configure<IOptions<MiddtIdentityOptions>>((authorization, identity) =>
        {
            // The admin role is configurable in the core options (MiddtIdentityOptions.AdminRole, default idp-admin).
            authorization.AddPolicy(MiddtIdentityUi.AdminPolicy, policy => policy
                .AddAuthenticationSchemes(MiddtIdentityDefaults.CookieScheme)
                .RequireRole(identity.Value.AdminRole is { Length: > 0 } role ? role : MiddtIdentityDefaults.AdminRole));
            authorization.AddPolicy(MiddtIdentityUi.AccountPolicy, policy => policy
                .AddAuthenticationSchemes(MiddtIdentityDefaults.CookieScheme)
                .RequireAuthenticatedUser());
        });
        services.AddRadzenComponents();
        services.AddCascadingAuthenticationState();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JourneyViewRegistry>();
        return services;
    }

    /// <summary>
    /// Content-Security-Policy of the identity provider: same-origin scripts (Blazor), inline style attributes (Radzen),
    /// no framing (login and consent must not be clickjacked). <c>form-action</c> is deliberately absent: browsers apply
    /// it to the redirects that follow a form post, and sign-in ends with a redirect to the client's
    /// <c>redirect_uri</c> on another origin.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    /// <summary>
    /// Security headers of the identity provider (<see cref="ContentSecurityPolicy"/>, <c>X-Frame-Options: DENY</c>,
    /// <c>nosniff</c>, referrer and permissions policy). Call it early, before authentication and the endpoints.
    /// </summary>
    public static IApplicationBuilder UseMiddtIdentitySecurityHeaders(this IApplicationBuilder app, Action<SecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = new SecurityHeadersOptions { ContentSecurityPolicy = ContentSecurityPolicy };
        configure?.Invoke(options);
        return app.UseMiddtSecurityHeaders(options);
    }

    /// <summary>Maps the identity UI with the library's root component <see cref="MiddtIdentityApp"/>.</summary>
    public static RazorComponentsEndpointConventionBuilder MapMiddtIdentityUi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapMiddtIdentityUi<MiddtIdentityApp>();

    /// <summary>
    /// Maps the identity UI with a host provided root component <typeparamref name="TApp"/> (interactive server render
    /// mode; the library and <see cref="MiddtIdentityUiOptions.AdditionalAssemblies"/> are added as route assemblies).
    /// </summary>
    public static RazorComponentsEndpointConventionBuilder MapMiddtIdentityUi<TApp>(this IEndpointRouteBuilder endpoints) where TApp : IComponent
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<MiddtIdentityUiOptions>>().Value;
        var builder = endpoints.MapRazorComponents<TApp>().AddInteractiveServerRenderMode();
        var assemblies = options.AdditionalAssemblies.Prepend(typeof(MiddtIdentityUi).Assembly)
            .Where(a => a != typeof(TApp).Assembly)
            .Distinct()
            .ToArray();
        if (assemblies.Length > 0)
        {
            builder.AddAdditionalAssemblies(assemblies);
        }

        return builder;
    }
}
