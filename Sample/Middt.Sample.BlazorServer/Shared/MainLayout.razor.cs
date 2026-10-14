using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Model.Configuration;
using Middt.Sample.BlazorServer.Service;
using Radzen;
using Radzen.Blazor;

namespace Middt.Sample.BlazorServer.Shared;

public partial class MainLayout : LayoutComponentBase
{
    private static readonly string[] themes = ["material", "standard", "default", "humanistic", "software", "fluent"];

    private bool sidebarExpanded = true;

    [Inject]
    private ThemeService ThemeService { get; set; } = default!;

    [Inject]
    private MenuService MenuService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    private BaseTokenHelper BaseTokenHelper { get; set; } = default!;

    [Inject]
    private IOptions<SecuritySettings> SecuritySettings { get; set; } = default!;

    protected override Task OnInitializedAsync() => BaseTokenHelper.LoadAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var current = MenuService.FindCurrent(NavigationManager.ToAbsoluteUri(NavigationManager.Uri));
        await JsRuntime.InvokeVoidAsync("middt.setTitle", MenuService.TitleFor(current));
    }

    private void OnUserButtonClick(RadzenSplitButtonItem? item)
    {
        if (item?.Value == "logout")
        {
            // Logout is a server rendered page (POST + antiforgery) that deletes the HttpOnly cookie.
            NavigationManager.NavigateTo(SecuritySettings.Value.LogoutPageURL, forceLoad: true);
        }
    }
}
