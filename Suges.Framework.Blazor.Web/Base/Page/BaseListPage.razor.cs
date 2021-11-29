using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public partial class BaseListPage<TService, TInterface, TModel> : BaseListPageComponentClass<TService, TInterface, TModel>
                where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {
    }
}
