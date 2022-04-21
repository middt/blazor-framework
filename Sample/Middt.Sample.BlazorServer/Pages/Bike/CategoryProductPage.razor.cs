using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using System.Threading.Tasks;

namespace Middt.Sample.BlazorServer.Pages.Bike
{


    public partial class CategoryProductPage : BaseDetailPageCode<CategoryService, ICategoryService, Category>
    {
        public BaseRadzenListPage<ProductService, IProductService, Product> baseRadzenListPage { get; set; }

        public override async Task OnAfterSearch()
        {
            await base.OnAfterSearch();

            if (Model != null)
            {
                baseRadzenListPage.SearchRequestModel.RequestModel.CategoryId = Model.CategoryId;
                await baseRadzenListPage.Search();
            }
        }
    }
}
