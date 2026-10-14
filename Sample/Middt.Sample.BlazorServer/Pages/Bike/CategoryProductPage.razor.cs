using Middt.Framework.Blazor.Web.Base.Page;

namespace Middt.Sample.BlazorServer.Pages.Bike;

public partial class CategoryProductPage
{
    private BaseRadzenListPage<ProductService, Product> productListPage = default!;

    public override async Task OnAfterSearch()
    {
        await base.OnAfterSearch();

        if (baseDetailPage.Model is { } category)
        {
            productListPage.SearchRequestModel.RequestModel.CategoryId = category.CategoryId;
            await productListPage.Search();
        }
    }
}
