using Middt.Framework.Client.Http;
using Middt.Demo.Blazor.Model;

namespace Middt.Demo.Blazor.Service;

/// <summary>
/// Side menu: home, the "İş Akışları" group (loaded dynamically from <c>GET /api/workflows</c>, one generic
/// <c>/workflow/{key}</c> page per workflow JSON definition), every JSON layout page with a <c>menu</c> entry and the layout editor.
/// </summary>
public sealed class MenuService(UiApiClient uiClient, WorkflowApiClient workflowClient, ILogger<MenuService> logger)
{
    private static readonly MenuItem Home = new() { Name = "Home", Path = "/", Icon = "home", Title = "Middt Sample" };

    private static readonly MenuItem Designer = new() { Name = "Ekran Tasarımı", Path = "/ui-admin", Icon = "dashboard_customize", Title = "Ekran Tasarımı" };

    // Per circuit (scoped service): the group's Expanded state and Children (loaded from GET /api/workflows) are mutable.
    private readonly MenuItem workflowGroup = new()
    {
        Name = "İş Akışları",
        Path = "/workflow",
        Icon = "account_tree",
        Expanded = true,
        Children = [],
    };

    private IReadOnlyList<MenuItem> layoutItems = [];
    private bool canDesign;

    public event Action? Changed;

    public IReadOnlyList<MenuItem> MenuItems => canDesign
        ? [Home, workflowGroup, .. layoutItems, Designer]
        : [Home, workflowGroup, .. layoutItems];

    /// <summary>Shows or hides the layout editor entry (<c>/ui-admin</c>) based on the <c>ui-admin</c> role.</summary>
    public void SetCanDesign(bool value) => canDesign = value;

    /// <summary>Route of the generic workflow page.</summary>
    public static string WorkflowPath(string key) => $"/workflow/{key}";

    /// <summary>The workflow menu entries loaded from <c>GET /api/workflows</c>.</summary>
    public IReadOnlyList<MenuItem> WorkflowItems => workflowGroup.Children ?? [];

    /// <summary>The layout page entries loaded from <c>GET /api/ui/layouts?kind=page</c>.</summary>
    public IReadOnlyList<MenuItem> LayoutItems => layoutItems;

    /// <summary>Uses entries loaded earlier (e.g. while prerendering) instead of calling the API again; does not raise <see cref="Changed"/>.</summary>
    public void Restore(IReadOnlyList<MenuItem> workflows, IReadOnlyList<MenuItem> layouts)
    {
        workflowGroup.Children = [.. workflows];
        layoutItems = layouts;
    }

    public async Task RefreshAsync()
    {
        var wfTask = workflowClient.ListWorkflowsAsync();
        var uiTask = uiClient.ListAsync("page");
        await Task.WhenAll(wfTask, uiTask);

        var wfResponse = await wfTask;
        if (wfResponse.IsSuccess && wfResponse.Data is { Count: > 0 } workflows)
        {
            workflowGroup.Children = [.. workflows.Select(w => new MenuItem
            {
                Name = w.Title ?? w.Key,
                Path = WorkflowPath(w.Key),
                Icon = w.Icon ?? "account_tree",
                Title = w.Title ?? w.Key,
            })];
        }

        var response = await uiTask;
        if (!response.IsSuccess)
        {
            logger.LogWarning("Layout menu could not be loaded: {Error}", response.ErrorText);
            Changed?.Invoke();
            return;
        }

        layoutItems = [.. (response.Data ?? [])
            .Where(l => l.Menu is not null)
            .Select(l => new MenuItem { Name = l.Menu!.Title ?? l.Title ?? l.Key, Path = $"/ui/{l.Key}", Icon = l.Menu.Icon, Title = l.Title })];
        Changed?.Invoke();
    }

    public MenuItem? FindCurrent(Uri uri) =>
        MenuItems.SelectMany(item => item.Children ?? [item])
            .FirstOrDefault(item => string.Equals(item.Path, uri.AbsolutePath, StringComparison.OrdinalIgnoreCase));

    public static string TitleFor(MenuItem? item) => item?.Title ?? "Middt Sample";
}

