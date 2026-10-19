using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Configuration;
using Middt.Sample.BlazorServer.Model;
using Middt.Sample.BlazorServer.Service;
using Radzen;
using Radzen.Blazor;
using Middt.Framework.Blazor.Web.Security;

namespace Middt.Sample.BlazorServer.Shared;

public sealed partial class MainLayout : LayoutComponentBase, IDisposable
{
    private static readonly string[] themes = ["material", "standard", "default", "humanistic", "software", "fluent"];

    private bool sidebarExpanded = true;

    [Inject]
    private ThemeService ThemeService { get; set; } = default!;

    [Inject]
    private MenuService MenuService { get; set; } = default!;

    [Inject]
    private Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    private BaseTokenHelper BaseTokenHelper { get; set; } = default!;

    [Inject]
    private IOptions<SecuritySettings> SecuritySettings { get; set; } = default!;

    private const string MenuStateKey = "middt-sample-menu";
    private const string WorkflowMenuStateKey = "middt-sample-workflows";

    /// <summary>Radzen theme of the host page (<c>_Host.cshtml</c> renders <c>RadzenTheme</c> statically with it).</summary>
    public const string DefaultTheme = "material";

    // The host page's <title> (TitleFor(null)); JS is only called when a page needs another one.
    private string? title = MenuService.TitleFor(null);
    private string? titlePath;
    private PersistingComponentStateSubscription persisting;

    [Inject]
    private PersistentComponentState ApplicationState { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        // RadzenTheme is rendered statically by the host page: tell this circuit's ThemeService which theme is shown (no JS call).
        if (ThemeService.Theme is null)
        {
            ThemeService.SetTheme(new ThemeOptions { Theme = DefaultTheme, TriggerChange = false });
        }

        await BaseTokenHelper.LoadAsync();
        persisting = ApplicationState.RegisterOnPersisting(() =>
        {
            if (MenuService.WorkflowItems.Count > 0 || MenuService.LayoutItems.Count > 0) // nothing loaded (anonymous, API error): the circuit loads it itself
            {
                ApplicationState.PersistAsJson(WorkflowMenuStateKey, MenuService.WorkflowItems);
                ApplicationState.PersistAsJson(MenuStateKey, MenuService.LayoutItems);
            }

            return Task.CompletedTask;
        });

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        MenuService.SetCanDesign(authState.User.IsInRole("ui-admin"));

        // The menu loaded while prerendering is handed over to the circuit, so it is not requested twice per page load.
        var hasWorkflows = ApplicationState.TryTakeFromJson<List<MenuItem>>(WorkflowMenuStateKey, out var workflows);
        var hasLayouts = ApplicationState.TryTakeFromJson<List<MenuItem>>(MenuStateKey, out var items);
        if ((hasWorkflows || hasLayouts) && (workflows is not null || items is not null))
        {
            MenuService.Restore(workflows ?? [], items ?? []);
        }
        else if (authState.User.Identity?.IsAuthenticated == true)
        {
            await MenuService.RefreshAsync();
        }

        // Subscribed after the initial load: that load is rendered anyway, Changed is for later refreshes (layout editor).
        MenuService.Changed += OnMenuChanged;
    }

    private void OnMenuChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        MenuService.Changed -= OnMenuChanged;
        persisting.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only when the title or the page changed: every render (menu, theme, navigation) ends here. Pages may set a more
        // specific title themselves (the workflow page uses the workflow title), so a new path always resets it.
        var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
        var current = MenuService.TitleFor(MenuService.FindCurrent(uri));
        if (current != title || !string.Equals(uri.AbsolutePath, titlePath, StringComparison.OrdinalIgnoreCase))
        {
            var unchanged = current == title;
            title = current;
            titlePath = uri.AbsolutePath;
            if (!unchanged || !firstRender)
            {
                await JsRuntime.InvokeVoidAsync("middt.setTitle", current);
            }
        }
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
