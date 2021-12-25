using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public partial class BaseListPage<TService, TInterface, TModel> : BaseListPageComponentClass<TService, TInterface, TModel>
                where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {
    }
}
