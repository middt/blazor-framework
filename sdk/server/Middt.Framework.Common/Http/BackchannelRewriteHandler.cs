namespace Middt.Framework.Common.Http;

/// <summary>
/// Sends server-to-server calls for a <b>public</b> authority (e.g. <c>http://localhost:8083/</c>, the issuer that
/// browsers and tokens use) to a <b>backchannel</b> authority (e.g. <c>http://middt-identity:8080/</c>, reachable
/// from inside a container network), keeping path and query. Issuer and metadata URLs stay public everywhere (token
/// validation compares them), only the transport target changes. Without both authorities the handler passes requests
/// through unchanged.
/// </summary>
public sealed class BackchannelRewriteHandler : DelegatingHandler
{
    private readonly Uri? publicAuthority;
    private readonly Uri? backchannelAuthority;

    /// <summary>Creates the handler; <see cref="DelegatingHandler.InnerHandler"/> is set by the caller (e.g. IHttpClientFactory).</summary>
    public BackchannelRewriteHandler(string? publicAuthority, string? backchannelAuthority)
    {
        this.publicAuthority = Parse(publicAuthority);
        this.backchannelAuthority = Parse(backchannelAuthority);
    }

    /// <summary>Creates the handler around <paramref name="innerHandler"/>.</summary>
    public BackchannelRewriteHandler(string? publicAuthority, string? backchannelAuthority, HttpMessageHandler innerHandler)
        : this(publicAuthority, backchannelAuthority)
    {
        InnerHandler = innerHandler;
    }

    /// <summary><c>true</c> when both authorities are configured, i.e. requests may be rewritten.</summary>
    public bool IsEnabled => publicAuthority is not null && backchannelAuthority is not null;

    /// <summary>
    /// Returns <paramref name="uri"/> on the backchannel authority when it starts with the public authority (same scheme,
    /// host, port and path prefix); otherwise <paramref name="uri"/> itself.
    /// </summary>
    public Uri Rewrite(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (publicAuthority is null || backchannelAuthority is null || !uri.IsAbsoluteUri)
        {
            return uri;
        }

        if (!string.Equals(uri.Scheme, publicAuthority.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.IdnHost, publicAuthority.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.Port != publicAuthority.Port)
        {
            return uri;
        }

        var path = uri.AbsolutePath;
        var prefix = publicAuthority.AbsolutePath;
        if (!(path + "/").StartsWith(prefix, StringComparison.Ordinal))
        {
            return uri;
        }

        // "/realm" (the authority itself without its trailing slash) maps to the backchannel path without one as well.
        var targetPath = path.Length >= prefix.Length
            ? backchannelAuthority.AbsolutePath + path[prefix.Length..]
            : backchannelAuthority.AbsolutePath.TrimEnd('/');

        // AbsolutePath and Query are in escaped form: concatenating keeps the encoding unchanged.
        return new Uri(backchannelAuthority.GetLeftPart(UriPartial.Authority) + targetPath + uri.Query);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is { } uri)
        {
            request.RequestUri = Rewrite(uri);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Absolute URI with a trailing slash (path prefix match), or <c>null</c> when empty.</summary>
    private static Uri? Parse(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{authority}' is not an absolute http(s) URI.", nameof(authority));
        }

        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");
    }
}
