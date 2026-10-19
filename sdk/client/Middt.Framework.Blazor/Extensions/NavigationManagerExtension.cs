using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Extensions;

public static class NavigationManagerExtension
{
    /// <summary>
    /// Navigates (full page load) to the server rendered login page, preserving the current location as <c>returnUrl</c>.
    /// </summary>
    public static void NavigateToLogin(this NavigationManager navigationManager, string loginPageUrl)
    {
        var returnUrl = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri);
        navigationManager.NavigateTo($"{loginPageUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
    }
}
