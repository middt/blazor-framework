using Middt.Framework.Client.Http;
using Middt.Sample.BlazorServer.Model;

namespace Middt.Sample.BlazorServer.Service;

/// <summary>Side menu: fixed entries plus every JSON layout page with a <c>menu</c> entry (loaded from the API).</summary>
public sealed class MenuService(UiApiClient uiClient, ILogger<MenuService> logger)
{
    private static readonly MenuItem Home = new() { Name = "Home", Path = "/", Icon = "home", Title = "Middt Sample" };

    private static readonly MenuItem[] Fixed =
    [
        new() { Name = "Sipariş Onayı", Path = "/Workflow/OrderApproval", Icon = "account_tree", Title = "Sipariş Onayı" },
        new() { Name = "Ekran Tasarımı", Path = "/ui-admin", Icon = "dashboard_customize", Title = "Ekran Tasarımı" },
    ];

    private IReadOnlyList<MenuItem> layoutItems = [];

    public event Action? Changed;

    public IReadOnlyList<MenuItem> MenuItems => [Home, .. layoutItems, .. Fixed];

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
