using Middt.Framework.Client.Http;
using Middt.Sample.BlazorServer.Model;

namespace Middt.Sample.BlazorServer.Service;

/// <summary>
/// Side menu: home, the "İş Akışları" group (one generic <c>/workflow/{key}</c> page per demo workflow), every JSON layout
/// page with a <c>menu</c> entry (loaded from the API) and the layout editor.
/// </summary>
public sealed class MenuService(UiApiClient uiClient, ILogger<MenuService> logger)
{
    private static readonly MenuItem Home = new() { Name = "Home", Path = "/", Icon = "home", Title = "Middt Sample" };

    /// <summary>Demo workflows in process order (key, menu text, icon); the E2E suites rely on the texts and order.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, string Icon)> Workflows =
    [
        ("store-opening", "Mağaza açılışı", "storefront"),
        ("staff-onboarding", "Personel alımı", "badge"),
        ("category-request", "Kategori talebi", "category"),
        ("product-launch", "Ürün lansmanı", "new_releases"),
        ("stock-replenishment", "Stok ikmali", "inventory_2"),
        ("customer-onboarding", "Müşteri kaydı", "person_add"),
        ("order-approval", "Sipariş Onayı", "shopping_cart"),
        ("order-fulfillment", "Sipariş karşılama", "local_shipping"),
    ];

    private static readonly MenuItem Designer = new() { Name = "Ekran Tasarımı", Path = "/ui-admin", Icon = "dashboard_customize", Title = "Ekran Tasarımı" };

    // Per circuit (scoped service): the group's Expanded state is mutable.
    private readonly MenuItem workflowGroup = new()
    {
        Name = "İş Akışları",
        Path = "/workflow",
        Icon = "account_tree",
        Expanded = true,
        Children = [.. Workflows.Select(w => new MenuItem { Name = w.Name, Path = WorkflowPath(w.Key), Icon = w.Icon, Title = w.Name })],
    };

    private IReadOnlyList<MenuItem> layoutItems = [];

    public event Action? Changed;

    public IReadOnlyList<MenuItem> MenuItems => [Home, workflowGroup, .. layoutItems, Designer];

    /// <summary>Route of the generic workflow page.</summary>
    public static string WorkflowPath(string key) => $"/workflow/{key}";

    /// <summary>The entries loaded from the API (persisted from prerendering into the circuit).</summary>
    public IReadOnlyList<MenuItem> LayoutItems => layoutItems;

    /// <summary>Uses entries loaded earlier (e.g. while prerendering) instead of calling the API again; does not raise <see cref="Changed"/>.</summary>
    public void Restore(IReadOnlyList<MenuItem> items) => layoutItems = items;

    public async Task RefreshAsync()
    {
        var response = await uiClient.ListAsync("page");
        if (!response.IsSuccess)
        {
            logger.LogWarning("Layout menu could not be loaded: {Error}", response.ErrorText);
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
