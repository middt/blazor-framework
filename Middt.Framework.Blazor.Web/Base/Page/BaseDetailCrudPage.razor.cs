using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public partial class BaseDetailCrudPage<TService, TInterface, TModel> : BaseDetailCrudPageComponentClass<TService, TInterface, TModel>

                        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                if (Model != null)
                {
                    IsUpdate = true;
                }
                else
                {
                    IsUpdate = false;
                    New();
                }
            }
        }
    }
}
