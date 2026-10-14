using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Bff.Hosting;
using Middt.Framework.Bff.Oidc;
using Middt.Framework.Bff.Proxy;
using Middt.Framework.Bff.Security;
using Middt.Framework.Client.DependencyInjection;
using Middt.Framework.Common.DependencyInjection;
using Middt.Framework.Common.Web;
using Middt.Framework.Model.Configuration;

namespace Middt.Framework.Bff.DependencyInjection;

/// <summary>Options of the BFF.</summary>
public sealed class MiddtBffOptions
{
    /// <summary>Content-Security-Policy of SPA hosts (<see cref="MiddtBffExtensions.UseMiddtSpaBff"/>); <c>null</c> disables it.</summary>
    public string? ContentSecurityPolicy { get; set; } = SecurityHeadersOptions.SpaContentSecurityPolicy;

    /// <summary>
    /// Name of the HttpOnly auth cookie. Browsers do not separate cookies by port: give each app on the same host its own
    /// name (e.g. two sample frontends on localhost).
    /// </summary>
    public string CookieName { get; set; } = MiddtBffExtensions.AuthCookieName;

    /// <summary>Login attempts (form posts) per client IP per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>
    /// Login attempts per (normalised) username per minute, across all client IPs: limits distributed password guessing
    /// against one account. Checked in addition to <see cref="LoginAttemptsPerMinute"/>; <c>0</c> disables it.
    /// </summary>
    public int LoginAttemptsPerUsernamePerMinute { get; set; } = 10;

    /// <summary>
    /// Path segment sequences the API proxy never forwards (answered with <c>404</c>), matched case-insensitively anywhere
    /// in the proxied path. Default <c>token</c>: <c>/api/{version}/token/**</c> (login, refresh, revoke) would hand raw
    /// access/refresh tokens to browser JavaScript and bypass the BFF login rate limiter. Entries may span several
    /// segments, e.g. <c>"admin/export"</c>.
    /// </summary>
    public IList<string> ProxyDeniedPaths { get; } = ["token"];

    /// <summary>
    /// Brotli/Gzip response compression in SPA hosts (<see cref="MiddtBffExtensions.UseMiddtSpaBff"/>) for scripts, styles,
    /// SVG and JSON (incl. proxied API responses the API did not compress itself). HTML is not compressed (BREACH: the
    /// login page carries an antiforgery token next to the reflected return URL). Precompressed <c>.br</c>/<c>.gz</c>
    /// files next to static files are served when present.
    /// </summary>
    public bool ResponseCompression { get; set; } = true;

    /// <summary>Request paths answered with 401/403 instead of a redirect to the login page (API style endpoints).</summary>
    public IList<string> ApiPathPrefixes { get; } = ["/api", "/hubs", "/bff", "/_blazor"];

    /// <summary>
    /// OpenID Connect sign-in at an identity provider (bound from configuration <c>Bff:Oidc</c> before the configure
    /// callback runs). Off unless <see cref="MiddtBffOidcOptions.Authority"/> is set; then <c>/Account/Login</c> redirects to
    /// the provider and the password form is disabled.
    /// </summary>
    public MiddtBffOidcOptions Oidc { get; } = new();
}

/// <summary>
/// Backend-for-frontend: the browser holds only an HttpOnly cookie that identifies a server side session; the API tokens
/// stay in <see cref="IServerTokenStore"/>. Used by Blazor Server (<c>AddMiddtBlazor</c>) and by SPA hosts:
/// <code>
/// builder.AddMiddtSpaBff();      // cookie session + login pages + /api and /hubs proxy
/// var app = builder.Build();
/// app.UseMiddtSpaBff();          // security headers, auth, /bff/user, proxy, SPA fallback (index.html)
/// </code>
/// </summary>
public static class MiddtBffExtensions
{
    public const string LoginRateLimitPolicy = "middt-login";
    public const string AuthCookieName = "middt.auth";

    /// <summary>Cookie session, token store, login/logout pages, token client and rate limiting (shared by every frontend).</summary>
    public static WebApplicationBuilder AddMiddtBff(this WebApplicationBuilder builder, Action<MiddtBffOptions>? configure = null)
    {
        AddMiddtBffCore(builder, configure);
        return builder;
    }

    private static MiddtBffOptions AddMiddtBffCore(WebApplicationBuilder builder, Action<MiddtBffOptions>? configure)
    {
        var options = new MiddtBffOptions();
        builder.Configuration.GetSection(MiddtBffOidcOptions.SectionName).Bind(options.Oidc);
        configure?.Invoke(options);

        var services = builder.Services;
        var configuration = builder.Configuration;
        var securitySettings = configuration.GetSection(SecuritySettings.SectionName).Get<SecuritySettings>() ?? new();
        var generalSettings = configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new();

        services.AddSingleton(options);
        services.AddRazorPages();
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
        services.AddScoped<CookieSignInService>();
        services.AddScoped<BffTokenAccessor>();
        services.TryAddSingleton<LoginAttemptLimiter>();

        // No "Server: Kestrel" response header (fingerprinting).
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        var forwardedHeaders = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new();
        if (!builder.Environment.IsDevelopment() && forwardedHeaders.KnownProxies.Count == 0 && forwardedHeaders.KnownNetworks.Count == 0)
        {
            services.AddHostedService<ForwardedHeadersStartupWarning>();
        }

        // The cookie stays the default (challenge) scheme in both modes: page navigations are redirected to
        // /Account/Login (which challenges the identity provider in OIDC mode), API style paths get 401/403.
        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
        authentication.AddCookie(cookie =>
            {
                cookie.Cookie.Name = options.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                cookie.LoginPath = securitySettings.LoginPageURL;
                cookie.LogoutPath = securitySettings.LogoutPageURL;
                cookie.AccessDeniedPath = securitySettings.AccessDeniedPageURL;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(generalSettings.SessionAbsoluteTimeoutInMinute);
                cookie.SlidingExpiration = false;
                cookie.Events.OnValidatePrincipal = ValidateServerSessionAsync;
                // API style requests get status codes; page navigations are redirected to the login / access denied page.
                cookie.Events.OnRedirectToLogin = context => StatusOrRedirect(context, StatusCodes.Status401Unauthorized, options);
                cookie.Events.OnRedirectToAccessDenied = context => StatusOrRedirect(context, StatusCodes.Status403Forbidden, options);
            });
        services.AddAuthorization();

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
        if (options.Oidc.IsEnabled)
        {
            // After AddMiddtApiClients: replaces its ITokenService with the identity provider's token endpoints.
            BffOidc.Add(authentication, options.Oidc, options.CookieName, builder.Environment);
        }

        services.AddHealthChecks();
        return options;
    }

    /// <summary>BFF for single page applications: <see cref="AddMiddtBff"/> plus the <c>/api</c> and <c>/hubs</c> proxy to the API.</summary>
    public static WebApplicationBuilder AddMiddtSpaBff(this WebApplicationBuilder builder, Action<MiddtBffOptions>? configure = null)
    {
        var options = AddMiddtBffCore(builder, configure);
        if (options.ResponseCompression)
        {
            SpaHosting.AddCompression(builder.Services);
        }

        var apiUrl = builder.Configuration.GetSection(ApiSettings.SectionName).Get<ApiSettings>()?.URL
            ?? throw new InvalidOperationException("ApiSettings:URL is required for the BFF proxy.");
        builder.Services.AddMiddtBffProxy(apiUrl);
        return builder;
    }

    /// <summary>
    /// Pipeline of a SPA host: forwarded headers, error handling, security headers (CSP), response compression, static
    /// files of the SPA, authentication, login/logout pages, <c>GET /bff/user</c>, the API proxy and the SPA fallback
    /// (<c>index.html</c>, requires sign-in). <c>*.html</c> files are only served to signed-in users.
    /// </summary>
    public static WebApplication UseMiddtSpaBff(this WebApplication app, string fallbackFile = "index.html")
    {
        var options = app.Services.GetRequiredService<MiddtBffOptions>();

        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            // No stack traces / exception details for unhandled errors (e.g. a header value Kestrel refuses).
            app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = SpaHosting.WriteErrorAsync });
            app.UseHsts();
        }

        // Empty error responses of pages (404, 400, …) get a short body; API style paths keep their bare status codes.
        app.UseWhen(context => !IsApiPath(context.Request.Path, options), branch => branch.UseStatusCodePages());
        app.UseMiddtSecurityHeaders(new SecurityHeadersOptions { ContentSecurityPolicy = options.ContentSecurityPolicy });
        if (options.ResponseCompression)
        {
            app.UseResponseCompression();
        }

        app.UseWebSockets();
        // Scripts, styles, images: anonymous (the login page needs them). HTML files only after authentication (below).
        app.UseWhen(context => !SpaHosting.IsHtml(context.Request.Path), branch =>
        {
            if (options.ResponseCompression)
            {
                branch.UseMiddleware<PrecompressedStaticFiles>();
            }

            branch.UseStaticFiles(SpaHosting.StaticFileOptions(cacheable: true));
        });

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWhen(context => SpaHosting.IsHtml(context.Request.Path), branch =>
        {
            branch.Use(SpaHosting.HtmlGateAsync);
            branch.UseStaticFiles(SpaHosting.StaticFileOptions(cacheable: false));
        });

        app.MapStaticAssets().Add(SpaHosting.RequireAuthorizationForHtml);
        app.MapRazorPages();
        app.MapMiddtBffEndpoints();
        app.MapMiddtBffProxy();
        app.MapHealthChecks("/health");
        app.MapFallbackToFile(fallbackFile, SpaHosting.StaticFileOptions(cacheable: false)).RequireAuthorization();
        return app;
    }

    /// <summary><c>GET /bff/user</c>: the signed-in user's name, id and roles (anonymous: <c>isAuthenticated = false</c>).</summary>
    public static IEndpointConventionBuilder MapMiddtBffEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/bff/user", (HttpContext context) =>
            {
                // [ResponseCache] is an MVC filter and has no effect on minimal APIs: set the header explicitly.
                context.Response.Headers.CacheControl = "no-store";
                var user = context.User;
                return user.Identity?.IsAuthenticated == true
                    ? Results.Ok(new BffUser(
                        true,
                        user.FindFirst(ClaimTypes.Sid)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        user.Identity.Name,
                        [.. user.FindAll(ClaimTypes.Role).Select(c => c.Value)]))
                    : Results.Ok(new BffUser(false, null, null, []));
            })
            .AllowAnonymous();

    /// <summary>Rejects cookies whose server side session no longer exists (signed out, revoked, idle or absolute timeout).</summary>
    public static async Task ValidateServerSessionAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sessionId = context.Principal?.FindFirst(MiddtClaimTypes.SessionId)?.Value;
        var store = context.HttpContext.RequestServices.GetRequiredService<IServerTokenStore>();

        var tokens = sessionId is null ? null : await store.GetAsync(sessionId, touch: true, context.HttpContext.RequestAborted);
        if (tokens is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        // The proxy needs the same entry for the access token: one store read per request (see BffTokenAccessor).
        BffTokenAccessor.RememberSession(context.HttpContext, sessionId!, tokens);
    }

    private static bool IsApiPath(PathString path, MiddtBffOptions options) =>
        options.ApiPathPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static Task StatusOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int statusCode, MiddtBffOptions options)
    {
        if (IsApiPath(context.Request.Path, options))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Response of <c>GET /bff/user</c>.</summary>
public sealed record BffUser(bool IsAuthenticated, string? Id, string? Name, IReadOnlyList<string> Roles);
