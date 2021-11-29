using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Blazor.Web.Base.Component.Pagination;
using Suges.Framework.Blazor.Web.Base.Page;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base
{
    public class BaseListPageComponentClass<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {
        [Parameter]
        public bool HideSearchButton { get; set; }

        [Parameter]
        public RenderFragment SearchContent { get; set; }
        [Parameter]
        public RenderFragment ListContent { get; set; }


        [Inject]
        public TService Service { get; set; }

        public BaseSearchRequestModel<TModel> SearchRequestModel { get; set; }
        public BaseResponseDataModel<List<TModel>> SearchResultModel { get; set; }

        [Parameter]
        public string SearchMethod { get; set; } = "GetItems";

        [Parameter]
        public bool IsFirstLoad { get; set; } = true;


        public Action OnAfterSearch { get; set; }

        public BaseListPageComponentClass()
        {
            SearchRequestModel = new BaseSearchRequestModel<TModel>();
            HideSearchButton = false;
        }

        protected void AraButton()
        {
            if (SearchRequestModel != null)
            {
                SearchRequestModel.CurrentPage = 1;

            }
            Search();
        }
        public virtual void Search()
        {
            ExecuteMethod(() =>
            {
                Type serviceType = Service.GetType();
                MethodInfo searchMethod = serviceType.GetMethods()
              //narrow the search before doing 'Single()'
              .Single(mi => mi.Name == SearchMethod
                         && mi.GetParameters().Length == 1);

                SearchResultModel = searchMethod.Invoke(Service, new object[] { SearchRequestModel }) as BaseResponseDataModel<List<TModel>>;

                //SearchResultModel = Service.GetItems(SearchRequestModel);

                if (SearchResultModel.Result != ResultEnum.Success)
                {
                    Log.Error(SearchResultModel.ErrorText);
                    Notification.ShowErrorMessage("Bilgiler Kaydedilirken bir hata oluştu.", SearchResultModel.MessageList.FirstOrDefault());
                }
                else
                {
                    if (SearchResultModel.Data == null || SearchResultModel.Data.Count < 1)
                    {
                        Notification.ShowInfoMessage("Bilgi", "Kayıt bulunamadı.");
                    }
                }

                AfterSearch();
                OnAfterSearch?.Invoke();
            });
        }

        public virtual void Cancel()
        {
            SearchRequestModel = new BaseSearchRequestModel<TModel>();
        }
        protected virtual void AfterSearch()
        { 
        }
    }
}
