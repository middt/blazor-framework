using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Middt.Framework.Common.Web;

public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds OWASP recommended response headers: Content-Security-Policy, X-Content-Type-Options,
    /// X-Frame-Options, Referrer-Policy, Permissions-Policy and Cross-Origin-Opener-Policy.
    /// </summary>
    public static IApplicationBuilder UseMiddtSecurityHeaders(this IApplicationBuilder app, SecurityHeadersOptions options) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = options.FrameOptions;
                headers["Referrer-Policy"] = options.ReferrerPolicy;
                headers["Permissions-Policy"] = options.PermissionsPolicy;
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                if (options.ContentSecurityPolicy is { Length: > 0 } csp &&
                    !options.ContentSecurityPolicyExcludedPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
                {
                    headers.ContentSecurityPolicy = csp;
                }

                return Task.CompletedTask;
            });

            await next(context);
        });
}
