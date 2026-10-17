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

    /// <summary>Demo workflows showcasing JSON document persistence and all workflow engine capabilities.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, string Icon)> Workflows =
    [
        ("customer-onboarding", "Müşteri Kaydı & KYC", "person_add"),
        ("credit-allocation", "Kredi Tahsis & 4-Göz Onay", "account_balance"),
        ("order-payment-orchestration", "Sipariş & Ödeme Orkestrasyonu", "shopping_cart"),
        ("sla-escalation", "SLA & Eskalasyon Yönetimi", "timer"),
        ("risk-limit-eval", "Risk & Limit Servisi (SubFlow)", "verified_user"),
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
    private bool canDesign;

    public event Action? Changed;

    public IReadOnlyList<MenuItem> MenuItems => canDesign
        ? [Home, workflowGroup, .. layoutItems, Designer]
        : [Home, workflowGroup, .. layoutItems];

    /// <summary>Shows or hides the layout editor entry (<c>/ui-admin</c>) based on the <c>ui-admin</c> role.</summary>
    public void SetCanDesign(bool value) => canDesign = value;

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
