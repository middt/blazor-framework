using Middt.Sample.BlazorServer.Model;

namespace Middt.Sample.BlazorServer.Service;

public sealed class MenuService
{
    public IReadOnlyList<MenuItem> MenuItems { get; } =
    [
        new() { Name = "Home", Path = "/", Icon = "home", Title = "Middt Sample" },
        new() { Name = "Category", Path = "/Bike/Category", Icon = "category", Title = "Categories" },
        new() { Name = "Product", Path = "/Bike/Product", Icon = "pedal_bike", Title = "Products" },
        new() { Name = "Order", Path = "/Bike/Order", Icon = "receipt_long", Title = "Orders" },
        new() { Name = "Sipariş Onayı", Path = "/Workflow/OrderApproval", Icon = "account_tree", Title = "Sipariş Onayı" },
    ];

    public MenuItem? FindCurrent(Uri uri) =>
        MenuItems.SelectMany(item => item.Children ?? [item])
            .FirstOrDefault(item => string.Equals(item.Path, uri.AbsolutePath, StringComparison.OrdinalIgnoreCase));

    public static string TitleFor(MenuItem? item) => item?.Title ?? "Middt Sample";
}
