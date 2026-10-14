namespace Middt.Framework.Common.Web;

/// <summary>
/// Open-redirect protection for <c>returnUrl</c> style parameters (MVC's <c>Url.IsLocalUrl</c> rules, stricter): only
/// printable ASCII is accepted (<c>0x21</c>–<c>0x7E</c>). Browsers strip control characters and whitespace while parsing
/// (<c>"/\t/evil"</c> → <c>//evil</c>), and non-ASCII characters make <c>LocalRedirect</c> throw when writing the
/// <c>Location</c> header. Non-ASCII paths must be percent-encoded (as the cookie middleware does for its <c>ReturnUrl</c>).
/// </summary>
public static class LocalUrl
{
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // "/path" but not "//host" or "/\host" (protocol relative); "~/path" but not "~//host".
        if (url[0] == '/')
        {
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\' && !HasUnsafeCharacters(url));
        }

        if (url.Length > 1 && url[0] == '~' && url[1] == '/')
        {
            return url.Length == 2 || (url[2] != '/' && url[2] != '\\' && !HasUnsafeCharacters(url));
        }

        return false;
    }

    /// <summary>Returns <paramref name="url"/> when it is local, otherwise <paramref name="fallback"/>.</summary>
    public static string OrDefault(string? url, string fallback = "/") => IsLocal(url) ? url! : fallback;

    // Control characters, whitespace (incl. space) and anything beyond ASCII.
    private static bool HasUnsafeCharacters(string url) => url.Any(c => c is <= ' ' or > '~');
}
