using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public class BaseDetailPageCode<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {

        public TModel Model { get { return baseDetailPage.Model; } set { baseDetailPage.Model = value; } }

        public BaseDetailPage<TService, TInterface, TModel> baseDetailPage { get; set; }

        public TService Service { get { return baseDetailPage.Service; } }

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseDetailPage.id.HasValue || !string.IsNullOrEmpty(NavigationManager.QueryString(baseDetailPage.QueryStringID)))
                {
                    if (!string.IsNullOrEmpty(NavigationManager.QueryString(baseDetailPage.QueryStringID)))
                    {
                        baseDetailPage.id = Convert.ToInt32(NavigationManager.QueryString(baseDetailPage.QueryStringID));
                    }
                }

                if (baseDetailPage.IsFirstLoad)
                {
                    Search();
                }
            }
        }

        public override void ExecuteMethod(Action action)
        {
                baseDetailPage.ExecuteMethod(action);
        }

        public virtual void Search()
        {
            baseDetailPage.Search();
        }
    }
}
