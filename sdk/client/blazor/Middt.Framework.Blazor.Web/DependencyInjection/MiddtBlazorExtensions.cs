using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Web.Base.Notification;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Common.DependencyInjection;
using Middt.Framework.Common.Licence;
using Middt.Framework.Client.Security;
using Middt.Framework.Common.Web;
using Middt.Framework.Model.Model.Configuration;
using Radzen;
using Syncfusion.Blazor;
using Middt.Framework.Client.DependencyInjection;

namespace Middt.Framework.Blazor.Web.DependencyInjection;

/// <summary>Options for <see cref="MiddtBlazorExtensions.AddMiddtBlazor"/>.</summary>
public sealed class MiddtBlazorOptions
{
    /// <summary>Content-Security-Policy of the app; <c>null</c> disables it.</summary>
    public string? ContentSecurityPolicy { get; set; } = SecurityHeadersOptions.BlazorServerContentSecurityPolicy;

    /// <summary>Login attempts per client IP per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>
    /// Registers the Syncfusion Blazor services and licence (configuration key <c>Syncfusion:LicenseKey</c>), needed only by
    /// <see cref="Base.Component.FileUpload.BaseFileUploadComponent"/>. Off by default: Syncfusion's theme (~4.7 MB) and script
    /// (~6.9 MB) must then also be added to the host page, which costs every page load. Same as calling
    /// <see cref="MiddtBlazorExtensions.AddMiddtSyncfusion"/>.
    /// </summary>
    public bool UseSyncfusion { get; set; }

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
/// builder.Services.AddMiddtApiClient&lt;CategoryService&gt;();
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
        var configuration = builder.Configuration;

        // Cookie session, server side token store, login/logout pages, token client, rate limiting (shared with SPA hosts).
        builder.AddMiddtBff(bff => bff.LoginAttemptsPerMinute = options.LoginAttemptsPerMinute);

        services.AddSingleton(options);
        services.AddServerSideBlazor(o => o.DetailedErrors = builder.Environment.IsDevelopment());

        services.AddRadzenComponents();
        if (options.UseSyncfusion)
        {
            services.AddMiddtSyncfusion(configuration);
        }

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

    /// <summary>
    /// Registers Syncfusion Blazor (services + licence from <c>Syncfusion:LicenseKey</c>) for
    /// <see cref="Base.Component.FileUpload.BaseFileUploadComponent"/>. Pages using it must also include
    /// <c>_content/Syncfusion.Blazor.Themes/bootstrap5.css</c> and <c>_content/Syncfusion.Blazor.Core/scripts/syncfusion-blazor.min.js</c>
    /// (ideally only on those pages, e.g. via <c>HeadContent</c>/a dedicated layout, not in the shared host page).
    /// </summary>
    public static IServiceCollection AddMiddtSyncfusion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSyncfusionBlazor();
        BaseLicence.Register(configuration["Syncfusion:LicenseKey"]);
        return services;
    }

    /// <summary>
    /// Registers the workflow view components (server driven UI) used by <c>WorkflowInstanceDetail</c>:
    /// <c>builder.Services.AddMiddtWorkflowViews(WorkflowViewCatalog.FromAssemblyOf&lt;OrderReviewView&gt;());</c>
    /// </summary>
    public static IServiceCollection AddMiddtWorkflowViews(this IServiceCollection services, Middt.Framework.Workflow.Razor.WorkflowViewCatalog catalog) =>
        services.AddSingleton(catalog);

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
