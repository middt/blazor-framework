using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Middt.Framework.Bff.Hosting;

/// <summary>Static file, caching and compression building blocks of <c>UseMiddtSpaBff</c>.</summary>
internal static partial class SpaHosting
{
    /// <summary>Content types compressed on the fly. Not <c>text/html</c> (BREACH, see <c>MiddtBffOptions.ResponseCompression</c>).</summary>
    internal static readonly string[] CompressedMimeTypes =
    [
        "application/json",
        "application/problem+json",
        "application/javascript",
        "text/javascript",
        "text/css",
        "image/svg+xml",
        "text/plain",
        "application/xml",
        "text/xml",
        "application/manifest+json",
        "application/wasm",
    ];

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void AddCompression(IServiceCollection services)
    {
        services.AddResponseCompression(compression =>
        {
            compression.EnableForHttps = true;
            compression.Providers.Add<BrotliCompressionProvider>();
            compression.Providers.Add<GzipCompressionProvider>();
            compression.MimeTypes = CompressedMimeTypes;
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    }

    /// <summary><c>*.html</c>/<c>*.htm</c>, also as a precompressed variant (<c>.html.br</c>) or with trailing dots (Windows).</summary>
    public static bool IsHtml(PathString path)
    {
        if (path.Value is not { } value)
        {
            return false;
        }

        value = PrecompressedStaticFiles.StripEncodingExtension(value.TrimEnd('.', ' ', '/'));
        return value.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Static files of the SPA. <paramref name="cacheable"/>: hashed bundle names (<c>main-ABCD1234.js</c>, <c>media/</c>)
    /// are cached for a year as immutable, everything else (favicon, <c>_content/…</c>, HTML) is revalidated (ETag).
    /// </summary>
    public static StaticFileOptions StaticFileOptions(bool cacheable) => new()
    {
        ContentTypeProvider = PrecompressedStaticFiles.ContentTypeProvider,
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.Headers;
            if (context.Context.Items[PrecompressedStaticFiles.EncodingItem] is string encoding)
            {
                headers.ContentEncoding = encoding;
                headers.Vary = "Accept-Encoding";
            }

            headers.CacheControl = cacheable && IsHashed(context.Context.Request.Path, context.File.Name)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
        },
    };

    /// <summary>Content hashed file names of the Angular CLI (<c>name-XXXXXXXX.ext</c>) and its <c>media/</c> folder.</summary>
    internal static bool IsHashed(PathString requestPath, string fileName) =>
        requestPath.StartsWithSegments("/media", StringComparison.OrdinalIgnoreCase)
        || HashedFileName().IsMatch(PrecompressedStaticFiles.StripEncodingExtension(fileName));

    /// <summary>
    /// HTML files are the app shell: anonymous visitors are sent to the login page like the SPA fallback, whoever would
    /// serve the file (the static file middleware or a static asset endpoint, incl. its Development fallback). HTML is
    /// always revalidated.
    /// </summary>
    public static async Task HtmlGateAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync();
            return;
        }

        context.Response.OnStarting(() =>
        {
            if (string.IsNullOrEmpty(context.Response.Headers.CacheControl))
            {
                context.Response.Headers.CacheControl = "no-cache";
            }

            return Task.CompletedTask;
        });
        await next(context);
    }

    /// <summary>Endpoint convention for <c>MapStaticAssets</c>: <c>*.html</c> assets of the manifest require a signed-in user.</summary>
    public static void RequireAuthorizationForHtml(EndpointBuilder endpoint)
    {
        if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: { } pattern } && IsHtml(new PathString("/" + pattern.TrimStart('/'))))
        {
            endpoint.Metadata.Add(new AuthorizeAttribute());
        }
    }

    public static Task WriteErrorAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("Beklenmeyen bir hata oluştu.");
    }

    internal static bool TryGetContentType(string path, out string contentType) =>
        ContentTypes.TryGetContentType(path, out contentType!);

    [GeneratedRegex(@"-[A-Z0-9]{8}\.[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HashedFileName();
}
