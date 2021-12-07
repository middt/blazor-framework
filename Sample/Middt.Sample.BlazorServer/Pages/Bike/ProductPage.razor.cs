using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;

namespace Middt.Sample.BlazorServer.Pages.Bike
{
    public partial class ProductPage : BaseListCrudPageCode<ProductService, IProductService, Product>
    {
    }
}
