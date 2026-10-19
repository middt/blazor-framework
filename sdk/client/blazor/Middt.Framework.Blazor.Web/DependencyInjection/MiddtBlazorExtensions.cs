using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Blazor.Web.Base.Notification;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Client.Security;
using Middt.Framework.Common.Web;
using Radzen;

namespace Middt.Framework.Blazor.Web.DependencyInjection;

/// <summary>Options for <see cref="MiddtBlazorExtensions.AddMiddtBlazor"/>.</summary>
public sealed class MiddtBlazorOptions
{
    /// <summary>Content-Security-Policy of the app; <c>null</c> disables it.</summary>
    public string? ContentSecurityPolicy { get; set; } = SecurityHeadersOptions.BlazorServerContentSecurityPolicy;

    /// <summary>Login attempts per client IP per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>
    /// Brotli/Gzip compression (fastest level) of dynamic responses such as the prerendered host page and JSON. Static web
    /// assets are served precompressed by <c>MapStaticAssets</c>; the Blazor circuit (<c>/_blazor</c>) and the login/logout pages
    /// (<c>/Account/*</c>: antiforgery token next to reflected input, BREACH) are excluded. Disable it when a reverse proxy compresses.
    /// </summary>
    public bool UseResponseCompression { get; set; } = true;
}

/// <summary>
/// Minimal-hosting entry points that replace the former <c>BaseBlazorStartup</c> class.
/// Authentication follows the BFF pattern (Middt.Framework.Bff): an HttpOnly cookie identifies a server side session that holds the API
/// tokens, so no token is ever exposed to browser JavaScript or storage.
/// <code>
/// builder.AddMiddtBlazor();
/// var app = builder.Build();
/// app.UseMiddtBlazor();
/// app.Run();
/// </code>
/// </summary>
public static class MiddtBlazorExtensions
{
    public const string LoginRateLimitPolicy = MiddtBffExtensions.LoginRateLimitPolicy;
    public const string AuthCookieName = MiddtBffExtensions.AuthCookieName;

    public static WebApplicationBuilder AddMiddtBlazor(this WebApplicationBuilder builder, Action<MiddtBlazorOptions>? configure = null)
    {
        var options = new MiddtBlazorOptions();
        configure?.Invoke(options);

        var services = builder.Services;

        // Cookie session, server side token store, login/logout pages, token client, rate limiting (shared with SPA hosts).
        builder.AddMiddtBff(bff => bff.LoginAttemptsPerMinute = options.LoginAttemptsPerMinute);

        services.AddSingleton(options);
        services.AddServerSideBlazor(o => o.DetailedErrors = builder.Environment.IsDevelopment());

        services.AddRadzenComponents();

        if (options.UseResponseCompression)
        {
            services.AddResponseCompression(o =>
            {
                o.EnableForHttps = true; // see UseMiddtBlazor for the paths excluded because of BREACH
                o.Providers.Add<BrotliCompressionProvider>();
                o.Providers.Add<GzipCompressionProvider>();
            });
            services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
            services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        }

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, MiddtAuthenticationStateProvider>();

        services.AddScoped<IBaseSessionState, ServerSessionState>();
        services.AddScoped<BaseTokenHelper>();
        services.AddScoped<INotification, RadzenNotification>();

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

        if (options.UseResponseCompression)
        {
            // The circuit (websocket / long polling frames) is not compressed by this middleware; skip it entirely. The account
            // pages reflect the user name next to an antiforgery token, so they stay uncompressed (BREACH).
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase),
                branch => branch.UseResponseCompression());
        }

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        // .NET 9+: framework scripts (_framework/blazor.server.js) and RCL assets (_content/*) are static web assets
        // that are only served through MapStaticAssets in published apps (fingerprinted, compressed, cache headers).
        // WithStaticAssets lets the host page resolve "~/..." URLs (and asp-append-version) to the fingerprinted,
        // immutable-cached file names of the manifest.
        app.MapStaticAssets();
        app.MapRazorPages().WithStaticAssets();
        app.MapBlazorHub();
        app.MapHealthChecks("/health");
        app.MapFallbackToPage(hostPage);

        return app;
    }
}
