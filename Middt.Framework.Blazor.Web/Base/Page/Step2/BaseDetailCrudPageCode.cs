using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public class BaseDetailCrudPageCode<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {

        public TModel Model { get { return baseDetailCrudPage.Model; } set { baseDetailCrudPage.Model = value; } }

        public BaseDetailCrudPage<TService, TInterface, TModel> baseDetailCrudPage { get; set; }

        public TService Service { get { return baseDetailCrudPage.Service; } }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseDetailCrudPage != null)
                {
                    baseDetailCrudPage.OnBeforeSearch = EventCallback.Factory.Create(this, OnBeforeSearch);
                    baseDetailCrudPage.OnAfterSearch = EventCallback.Factory.Create(this, OnAfterSearch);
                }
            }
        }
        public override async Task ExecuteMethod(Action action)
        {
           await baseDetailCrudPage.ExecuteMethod(action);
        }


        public virtual async Task Search()
        {
            await baseDetailCrudPage.Search();
        }

        public virtual async Task SaveButtonClick()
        {
            await baseDetailCrudPage.SaveButtonClick();
        }

        public virtual async Task Edit(TModel model)
        {
            await baseDetailCrudPage.Edit(model);
        }
        public virtual async Task New()
        {
            await baseDetailCrudPage.New();
        }
        public virtual async Task OnAfterSearch()
        {

        }
        public virtual async Task OnBeforeSearch()
        {

        }
    }
}
