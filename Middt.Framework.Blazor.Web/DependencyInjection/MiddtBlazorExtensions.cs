using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Web.Base.Notification;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Common.DependencyInjection;
using Middt.Framework.Common.Licence;
using Middt.Framework.Common.Notification;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Web;
using Middt.Framework.Model.Model.Configuration;
using Radzen;
using Syncfusion.Blazor;

namespace Middt.Framework.Blazor.Web.DependencyInjection;

/// <summary>Options for <see cref="MiddtBlazorExtensions.AddMiddtBlazor"/>.</summary>
public sealed class MiddtBlazorOptions
{
    /// <summary>Content-Security-Policy of the app; <c>null</c> disables it.</summary>
    public string? ContentSecurityPolicy { get; set; } = SecurityHeadersOptions.BlazorServerContentSecurityPolicy;

    /// <summary>Login attempts per client IP per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;
}

/// <summary>
/// Minimal-hosting entry points that replace the former <c>BaseBlazorStartup</c> class.
/// Authentication follows the BFF pattern: an HttpOnly cookie identifies a server side session that holds the API
/// tokens, so no token is ever exposed to browser JavaScript or storage.
/// <code>
/// builder.AddMiddtBlazor();
/// builder.Services.AddMiddtApiClient&lt;CategoryService&gt;();
/// var app = builder.Build();
/// app.UseMiddtBlazor();
/// app.Run();
/// </code>
/// </summary>
public static class MiddtBlazorExtensions
{
    public const string LoginRateLimitPolicy = "middt-login";
    public const string AuthCookieName = "middt.auth";

    public static WebApplicationBuilder AddMiddtBlazor(this WebApplicationBuilder builder, Action<MiddtBlazorOptions>? configure = null)
    {
        var options = new MiddtBlazorOptions();
        configure?.Invoke(options);

        var services = builder.Services;
        var configuration = builder.Configuration;
        var securitySettings = configuration.GetSection(SecuritySettings.SectionName).Get<SecuritySettings>() ?? new();
        var generalSettings = configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new();

        services.AddSingleton(options);
        services.AddRazorPages();
        services.AddServerSideBlazor(o => o.DetailedErrors = builder.Environment.IsDevelopment());

        services.AddRadzenComponents();
        services.AddSyncfusionBlazor();
        BaseLicence.Register(configuration["Syncfusion:LicenseKey"]);

        services.Configure<GeneralSettings>(configuration.GetSection(GeneralSettings.SectionName));
        services.Configure<SecuritySettings>(configuration.GetSection(SecuritySettings.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddServicesFromConfiguration(configuration);
        services.AddMiddtForwardedHeaders(configuration);

        // Keys protect the auth cookie and the server side token store. Persist them (e.g. PersistKeysToStackExchangeRedis)
        // when running more than one instance or in containers, otherwise sessions are lost on restart.
        services.AddDataProtection().SetApplicationName(configuration["DataProtection:ApplicationName"] ?? builder.Environment.ApplicationName);

        // Server side session store: in-memory by default, shared when the app registers e.g. Redis as IDistributedCache.
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<IServerTokenStore, DistributedServerTokenStore>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = AuthCookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                cookie.LoginPath = securitySettings.LoginPageURL;
                cookie.LogoutPath = securitySettings.LogoutPageURL;
                cookie.AccessDeniedPath = securitySettings.LoginPageURL;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(generalSettings.SessionAbsoluteTimeoutInMinute);
                cookie.SlidingExpiration = false;
                cookie.Events.OnValidatePrincipal = ValidateServerSessionAsync;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, MiddtAuthenticationStateProvider>();

        services.AddScoped<CookieSignInService>();
        services.AddScoped<IBaseSessionState, ServerSessionState>();
        services.AddScoped<BaseTokenHelper>();
        services.AddScoped<INotification, RadzenNotification>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(LoginRateLimitPolicy, context =>
                // Only form posts count; rendering the login page is free.
                HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = options.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetNoLimiter("get"));
        });

        services.AddMiddtApiClients(configuration);
        services.AddHealthChecks();

        return builder;
    }

    public static WebApplication UseMiddtBlazor(this WebApplication app, string hostPage = "/_Host")
    {
        var options = app.Services.GetRequiredService<MiddtBlazorOptions>();

        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseMiddtSecurityHeaders(new SecurityHeadersOptions { ContentSecurityPolicy = options.ContentSecurityPolicy });

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        // .NET 9+: framework scripts (_framework/blazor.server.js) and RCL assets (_content/*) are static web assets
        // that are only served through MapStaticAssets in published apps (fingerprinted, compressed, cache headers).
        app.MapStaticAssets();
        app.MapRazorPages();
        app.MapBlazorHub();
        app.MapHealthChecks("/health");
        app.MapFallbackToPage(hostPage);

        return app;
    }

    /// <summary>Rejects cookies whose server side session no longer exists (signed out, revoked, idle or absolute timeout).</summary>
    private static async Task ValidateServerSessionAsync(CookieValidatePrincipalContext context)
    {
        var sessionId = context.Principal?.FindFirst(MiddtClaimTypes.SessionId)?.Value;
        var store = context.HttpContext.RequestServices.GetRequiredService<IServerTokenStore>();

        if (sessionId is null || await store.GetAsync(sessionId, touch: true, context.HttpContext.RequestAborted) is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
