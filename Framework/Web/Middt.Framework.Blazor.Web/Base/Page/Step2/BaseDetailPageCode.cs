using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;

namespace Middt.Framework.Blazor.Web.Base.Page
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
                if (baseDetailPage != null)
                {
                    baseDetailPage.OnBeforeSearch += OnBeforeSearch;
                    baseDetailPage.OnAfterSearch += OnAfterSearch;
                }
            }
        }

        public virtual void Search()
        {
            baseDetailPage.Search();
        }

        public override void ExecuteMethod(Action action)
        {
            baseDetailPage.ExecuteMethod(action);
        }

        public virtual void OnAfterSearch()
        {

        }
        public virtual void OnBeforeSearch()
        {

        }
    }
}
