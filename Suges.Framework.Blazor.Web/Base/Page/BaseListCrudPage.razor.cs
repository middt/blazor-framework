using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public partial class BaseListCrudPage<TService, TInterface, TModel> : BaseListCrudPageComponentClass<TService, TInterface, TModel>
                where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
    }
}
