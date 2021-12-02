using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Collections.Generic;

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

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseListPage != null)
                {
                    baseListPage.OnAfterSearch += AfterSearch;
                }
            }
        }

        public virtual void Search()
        {
            baseListPage.Search();
        }

        public override void ExecuteMethod(Action action)
        {
            baseListPage.ExecuteMethod(action);
        }

        public virtual void AfterSearch()
        {

        }
    }
}
