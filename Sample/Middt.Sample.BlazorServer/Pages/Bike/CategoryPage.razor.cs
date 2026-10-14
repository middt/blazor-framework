using System.Globalization;

namespace Middt.Sample.BlazorServer.Pages.Bike;

public partial class CategoryPage
{
    private void Detail(Category model) =>
        NavigationManager.NavigateTo($"/Bike/CategoryProduct?ID={model.CategoryId.ToString(CultureInfo.InvariantCulture)}");
}
