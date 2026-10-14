using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Middt.Framework.Common.Web;

/// <summary>
/// Trusted reverse proxies (configuration section <c>ForwardedHeaders</c>). Only requests coming from these
/// addresses may override the client IP / scheme via X-Forwarded-* headers; loopback is always trusted.
/// Client IPs drive rate limiting and audit logs, so never trust networks clients can reach directly.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Proxy IP addresses, e.g. <c>10.0.0.5</c>.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR notation, e.g. <c>10.0.0.0/8</c>.</summary>
    public List<string> KnownNetworks { get; set; } = [];

    public int ForwardLimit { get; set; } = 1;
}

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddMiddtForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;

            foreach (var proxy in settings.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in settings.KnownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
