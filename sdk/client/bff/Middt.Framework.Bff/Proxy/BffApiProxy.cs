using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Security;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Middt.Framework.Bff.Proxy;

/// <summary>
/// Forwards <c>/api/**</c> and <c>/hubs/**</c> (SignalR incl. WebSockets) to the API with the session's access token
/// (YARP). Browser cookies are not forwarded. CSRF: API calls must carry the <see cref="CsrfHeader"/> header (a cross-site
/// page cannot add it without a CORS preflight); WebSocket upgrades must come from the same origin. Paths matching
/// <see cref="MiddtBffOptions.ProxyDeniedPaths"/> (default: the token endpoints) are answered with 404 and never forwarded.
/// The browser's <c>Accept-Encoding</c> is forwarded, so the API's own compression reaches the browser unchanged.
/// </summary>
public static class BffApiProxy
{
    public const string CsrfHeader = "X-CSRF";
    private const string ClusterId = "middt-api";

    public static IServiceCollection AddMiddtBffProxy(this IServiceCollection services, string apiUrl)
    {
        RouteConfig Route(string id, string path) => new()
        {
            RouteId = id,
            ClusterId = ClusterId,
            AuthorizationPolicy = "default", // signed-in users only; anonymous → 401 (see MiddtBffOptions.ApiPathPrefixes)
            Match = new RouteMatch { Path = path },
        };

        services.AddReverseProxy()
            .LoadFromMemory(
                [Route("middt-api", "/api/{**catch-all}"), Route("middt-hubs", "/hubs/{**catch-all}")],
                [new ClusterConfig
                {
                    ClusterId = ClusterId,
                    Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal) { ["api"] = new() { Address = apiUrl } },
                }])
            .AddTransforms(context =>
            {
                context.AddRequestHeaderRemove("Cookie");
                context.AddRequestHeaderRemove(CsrfHeader);
                context.AddRequestTransform(async transform =>
                {
                    // Resolved by the proxy pipeline below and memoized per request: no second token store read.
                    var accessor = transform.HttpContext.RequestServices.GetRequiredService<BffTokenAccessor>();
                    var token = await accessor.GetAccessTokenAsync(transform.HttpContext, transform.HttpContext.RequestAborted);
                    transform.ProxyRequest.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
                });
            });
        return services;
    }

    public static ReverseProxyConventionBuilder MapMiddtBffProxy(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapReverseProxy(pipeline => pipeline.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<MiddtBffOptions>();
            if (IsDenied(context.Request.Path, options.ProxyDeniedPaths))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!IsTrustedRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // The session ended (e.g. refresh failed): answer 401 so the SPA sends the user to the login page.
            var accessor = context.RequestServices.GetRequiredService<BffTokenAccessor>();
            if (await accessor.GetAccessTokenAsync(context, context.RequestAborted) is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        }));

    /// <summary>True when <paramref name="path"/> contains one of the <paramref name="deniedPaths"/> segment sequences.</summary>
    public static bool IsDenied(PathString path, IEnumerable<string> deniedPaths)
    {
        // Request.Path is already percent-decoded (%74oken → token) and dot segments are resolved by the server.
        var segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var denied in deniedPaths)
        {
            var sequence = denied.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (sequence.Length == 0)
            {
                continue;
            }

            for (var start = 0; start + sequence.Length <= segments.Length; start++)
            {
                var match = true;
                for (var i = 0; i < sequence.Length && match; i++)
                {
                    match = string.Equals(segments[start + i].Trim(), sequence[i], StringComparison.OrdinalIgnoreCase);
                }

                if (match)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsTrustedRequest(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            // Browsers cannot set headers on WebSocket handshakes, but always send Origin.
            return context.Request.Headers.Origin.ToString() is { Length: > 0 } origin
                && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        return context.Request.Headers.ContainsKey(CsrfHeader);
    }
}
