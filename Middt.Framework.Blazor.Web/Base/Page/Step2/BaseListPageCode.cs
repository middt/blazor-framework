using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public class BaseListPageCode<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {

        public BaseListPageComponentClass<TService, TInterface, TModel> baseListPage { get; set; }

        public BaseSearchRequestModel<TModel> SearchRequestModel { get { return baseListPage.SearchRequestModel; } set { baseListPage.SearchRequestModel = value; } }

        public BaseResponseDataModel<List<TModel>> SearchResultModel { get { return baseListPage.SearchResultModel; } set { baseListPage.SearchResultModel = value; } }

        public TService Service { get { return baseListPage.Service; } }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseListPage != null)
                {
                    baseListPage.OnBeforeSearch = EventCallback.Factory.Create(this, OnBeforeSearch);
                    baseListPage.OnAfterSearch = EventCallback.Factory.Create(this, OnAfterSearch);
                }
            }
        }

        public virtual async Task Search()
        {
            await baseListPage.Search();
        }

        public override async Task ExecuteMethod(Action action)
        {
            await baseListPage.ExecuteMethod(action);
        }

        public virtual async Task OnAfterSearch()
        {

        }
        public virtual async Task OnBeforeSearch()
        {

        }
    }
}
