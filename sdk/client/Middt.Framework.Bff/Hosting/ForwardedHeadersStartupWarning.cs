using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Middt.Framework.Bff.Hosting;

/// <summary>
/// Outside Development without <c>ForwardedHeaders:KnownProxies</c>/<c>KnownNetworks</c>, X-Forwarded-For of a reverse
/// proxy is ignored: every user then shares the proxy's IP, so the per-IP login limiter throttles all users together.
/// </summary>
internal sealed partial class ForwardedHeadersStartupWarning(ILogger<ForwardedHeadersStartupWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogNoKnownProxies(logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "ForwardedHeaders:KnownProxies and ForwardedHeaders:KnownNetworks are empty: behind a reverse proxy all clients share the proxy's IP address (login rate limiting, audit logs). Configure the proxy addresses.")]
    private static partial void LogNoKnownProxies(ILogger logger);
}
