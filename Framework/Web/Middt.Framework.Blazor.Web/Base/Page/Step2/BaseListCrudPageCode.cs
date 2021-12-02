using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;
using System.Collections.Generic;

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


        public virtual void Edit(TModel model)
        {
            baseListCrudPage.Edit(model);
        }

        public virtual void Delete(TModel model)
        {
            baseListCrudPage.Delete(model);
        }



        public virtual void Search()
        {
            baseListCrudPage.Search();
        }

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                baseListCrudPage.OnBeforeSearch += OnBeforeSearch;
                baseListCrudPage.OnAfterSearch += OnAfterSearch;

                baseListCrudPage.OnBeforeModalOpen += OnBeforeModalOpen;
                baseListCrudPage.OnAfterModalClose += OnAfterModalClose;
            }
        }
        public virtual void OnBeforeSearch()
        {

        }

        public virtual void OnAfterSearch()
        {

        }

        public virtual void OnBeforeModalOpen()
        {

        }

        public virtual void OnAfterModalClose()
        {

        }
        public override void ExecuteMethod(Action action)
        {
            baseListCrudPage.ExecuteMethod(action);
        }
    }
}
