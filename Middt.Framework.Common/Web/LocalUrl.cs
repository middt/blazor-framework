namespace Middt.Framework.Common.Web;

/// <summary>Open-redirect protection for <c>returnUrl</c> style parameters (same rules as MVC's <c>Url.IsLocalUrl</c>).</summary>
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
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\' && !HasControlCharacters(url));
        }

        if (url.Length > 1 && url[0] == '~' && url[1] == '/')
        {
            return url.Length == 2 || (url[2] != '/' && url[2] != '\\' && !HasControlCharacters(url));
        }

        return false;
    }

    /// <summary>Returns <paramref name="url"/> when it is local, otherwise <paramref name="fallback"/>.</summary>
    public static string OrDefault(string? url, string fallback = "/") => IsLocal(url) ? url! : fallback;

    private static bool HasControlCharacters(string url) => url.Any(char.IsControl);
}
