using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System;

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

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (baseDetailCrudPage != null)
                {
                    baseDetailCrudPage.OnBeforeSearch += OnBeforeSearch;
                    baseDetailCrudPage.OnAfterSearch += OnAfterSearch;
                }
            }
        }
        public override void ExecuteMethod(Action action)
        {
            baseDetailCrudPage.ExecuteMethod(action);
        }


        public virtual void Search()
        {
            baseDetailCrudPage.Search();
        }

        public virtual void SaveButtonClick()
        {
            baseDetailCrudPage.SaveButtonClick();
        }

        public virtual void Edit(TModel model)
        {
            baseDetailCrudPage.Edit(model);
        }
        public virtual void New()
        {
            baseDetailCrudPage.New();
        }
        public virtual void OnAfterSearch()
        {

        }
        public virtual void OnBeforeSearch()
        {

        }
    }
}
