using Microsoft.AspNetCore.Http;

namespace Middt.Framework.Common.Http;

/// <summary>
/// Forwards the end user's IP (X-Forwarded-For) on server-to-server calls made while handling an HTTP request
/// (e.g. the BFF login post), so the API can rate limit and audit per real client. The API must list the
/// caller as a trusted proxy (<c>ForwardedHeaders</c> settings) for the header to be honoured.
/// </summary>
public sealed class ForwardedClientIpHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext?.Connection.RemoteIpAddress is { } clientIp)
        {
            request.Headers.Remove("X-Forwarded-For");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
