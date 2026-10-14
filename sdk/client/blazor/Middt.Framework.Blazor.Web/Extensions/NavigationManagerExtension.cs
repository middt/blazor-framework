using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Middt.Framework.Blazor.Web.Extensions;

public static class NavigationManagerExtension
{
    public static IDictionary<string, Microsoft.Extensions.Primitives.StringValues> QueryString(this NavigationManager navigationManager) =>
        QueryHelpers.ParseQuery(navigationManager.ToAbsoluteUri(navigationManager.Uri).Query);

    public static string? QueryString(this NavigationManager navigationManager, string key) =>
        navigationManager.QueryString().TryGetValue(key, out var value) ? value.ToString() : null;

    /// <summary>
    /// Navigates (full page load) to the server rendered login page, preserving the current location as <c>returnUrl</c>.
    /// </summary>
    public static void NavigateToLogin(this NavigationManager navigationManager, string loginPageUrl)
    {
        var returnUrl = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri);
        navigationManager.NavigateTo($"{loginPageUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
    }
}
