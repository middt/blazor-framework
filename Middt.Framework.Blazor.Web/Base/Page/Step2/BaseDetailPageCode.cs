using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Threading.Tasks;

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


        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
           await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseDetailPage != null)
                {
                    baseDetailPage.OnBeforeSearch = EventCallback.Factory.Create(this, OnBeforeSearch); 
                    baseDetailPage.OnAfterSearch = EventCallback.Factory.Create(this, OnAfterSearch); 
                }
            }
        }

        public virtual async Task Search()
        {
            await baseDetailPage.Search();
        }

        public override async Task ExecuteMethod(Action action)
        {
            await baseDetailPage.ExecuteMethod(action);
        }

        public virtual async Task OnAfterSearch()
        {

        }
        public virtual async Task OnBeforeSearch()
        {

        }
    }
}
