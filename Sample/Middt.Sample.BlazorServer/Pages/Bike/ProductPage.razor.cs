using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;

namespace Middt.Sample.BlazorServer.Pages.Bike
{
    public partial class ProductPage : BaseDetailPageCode<CategoryService, ICategoryService, Category>
    {
        public BaseRadzenListPage<ProductService, IProductService, Product> baseRadzenListPage { get; set; }

        public override void OnAfterSearch()
        {
            base.OnAfterSearch();

            baseRadzenListPage.SearchRequestModel.RequestModel.CategoryId = Model.CategoryId;
            baseRadzenListPage.Search();
        }
    }
}
