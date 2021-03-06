using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;

namespace Middt.Sample.BlazorServer.Pages.Bike
{
    public partial class CategoryPage : BaseListCrudPageCode<CategoryService, ICategoryService, Category>
    {
        public void Detail(Category model)
        {
            NavigationManager.NavigateTo("/Bike/CategoryProduct?ID=" + model.CategoryId);
        }
    }
}
