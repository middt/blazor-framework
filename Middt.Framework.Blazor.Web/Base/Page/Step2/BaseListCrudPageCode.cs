using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public class BaseListCrudPageCode<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        public BaseListCrudPageComponentClass<TService, TInterface, TModel> baseListCrudPage { get; set; }


        public TModel Model { get { return baseListCrudPage.Model; } set { baseListCrudPage.Model = value; } }

        public BaseSearchRequestModel<TModel> SearchRequestModel { get { return baseListCrudPage.SearchRequestModel; } set { baseListCrudPage.SearchRequestModel = value; } }

        public BaseResponseDataModel<List<TModel>> SearchResultModel { get { return baseListCrudPage.SearchResultModel; } set { baseListCrudPage.SearchResultModel = value; } }

        public TService Service { get { return baseListCrudPage.Service; } }


        public virtual async Task Edit(TModel model)
        {
            await baseListCrudPage.Edit(model);
        }

        public virtual async Task Delete(TModel model)
        {
            await baseListCrudPage.Delete(model);
        }



        public virtual async Task Search()
        {
           await baseListCrudPage.Search();
        }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                baseListCrudPage.OnBeforeSearch = EventCallback.Factory.Create(this, OnBeforeSearch);
                baseListCrudPage.OnAfterSearch = EventCallback.Factory.Create(this, OnAfterSearch);

                baseListCrudPage.OnBeforeModalOpen = EventCallback.Factory.Create(this, OnBeforeModalOpen);
                baseListCrudPage.OnAfterModalClose = EventCallback.Factory.Create(this, OnAfterModalClose);
            }
        }
        public virtual async Task OnBeforeSearch()
        {

        }

        public virtual async Task OnAfterSearch()
        {

        }

        public virtual async Task OnBeforeModalOpen()
        {

        }

        public virtual async Task OnAfterModalClose()
        {

        }
        public override async Task ExecuteMethod(Action action)
        {
            await baseListCrudPage.ExecuteMethod(action);
        }
    }
}
