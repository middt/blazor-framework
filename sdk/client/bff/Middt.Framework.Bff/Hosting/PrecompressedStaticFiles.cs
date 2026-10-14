using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Middt.Framework.Bff.Hosting;

/// <summary>
/// Serves <c>file.js.br</c> / <c>file.js.gz</c> (emitted at build time, e.g. by the Dockerfile) instead of <c>file.js</c>
/// when the browser accepts the encoding: maximum compression without CPU cost per request. Without precompressed files
/// the response compression middleware compresses on the fly.
/// </summary>
internal sealed class PrecompressedStaticFiles(RequestDelegate next, IWebHostEnvironment environment)
{
    public const string EncodingItem = "middt:precompressed";

    private static readonly (string Encoding, string Extension)[] Encodings = [("br", ".br"), ("gzip", ".gz")];

    private static readonly HashSet<string> Compressible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".svg", ".json", ".txt", ".xml", ".map", ".wasm",
    };

    /// <summary>Content type of <c>x.js.br</c> is the one of <c>x.js</c>.</summary>
    public static IContentTypeProvider ContentTypeProvider { get; } = new EncodedContentTypeProvider();

    public static string StripEncodingExtension(string fileName)
    {
        foreach (var (_, extension) in Encodings)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^extension.Length];
            }
        }

        return fileName;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && request.Path.Value is { } path
            && Compressible.Contains(Path.GetExtension(path))
            && request.Headers.AcceptEncoding.Count > 0)
        {
            var accepted = request.GetTypedHeaders().AcceptEncoding;
            foreach (var (encoding, extension) in Encodings)
            {
                if (Accepts(accepted, encoding) && environment.WebRootFileProvider.GetFileInfo(path + extension) is { Exists: true, IsDirectory: false })
                {
                    request.Path = new PathString(path + extension);
                    context.Items[EncodingItem] = encoding;
                    break;
                }
            }
        }

        return next(context);
    }

    private static bool Accepts(IList<StringWithQualityHeaderValue> accepted, string encoding) =>
        accepted.Any(value => value.Value.Equals(encoding, StringComparison.OrdinalIgnoreCase) && value.Quality is not 0);

    private sealed class EncodedContentTypeProvider : IContentTypeProvider
    {
        public bool TryGetContentType(string subpath, out string contentType) =>
            SpaHosting.TryGetContentType(StripEncodingExtension(subpath), out contentType);
    }
}
