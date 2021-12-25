using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public partial class BaseListCrudPage<TService, TInterface, TModel> : BaseListCrudPageComponentClass<TService, TInterface, TModel>
                where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
    }
}
