using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Pagination;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Model.Enumerations;
using System.Reflection;

namespace Middt.Framework.Blazor.Web.Base
{
    public class BaseListPageComponentClass<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
        where TService : BaseListRefit<TInterface, TModel>
    {
        [Parameter]
        public int PageSize { get; set; } = 20;


        [Parameter]
        public bool HideSearchButton { get; set; } = false;
        [Parameter]
        public bool HideSearch { get; set; } = false;


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

        public BasePaginationComponent Pagination { get; set; }

        public BaseListPageComponentClass()
        {
            SearchRequestModel = new BaseSearchRequestModel<TModel>();
        }
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                Pagination.OnPageChange += () =>
                {
                    GotoPage().Wait();
                };


                if (IsFirstLoad)
                {
                    Search();
                }
            }
        }
        public virtual async Task GotoPage()
        {
            Search();
        }

        protected void SearchButton()
        {
            SearchRequestModel.CurrentPage = 1;
            Search();
        }
        public virtual void Search()
        {
            ExecuteMethod(() =>
            {
                BeforeSearch();

                Type serviceType = Service.GetType();
                MethodInfo searchMethod = serviceType.GetMethods()


              .Single(mi => mi.Name == SearchMethod
                         && mi.GetParameters().Length == 1);

                SearchResultModel = searchMethod.Invoke(Service, new object[] { SearchRequestModel }) as BaseResponseDataModel<List<TModel>>;

                if (SearchResultModel.Result != ResultEnum.Success)
                {
                    Log.Error(SearchResultModel.ErrorText);
                    Notification.ShowErrorMessage("Arama yapılırken bir hata oluştu.", SearchResultModel.MessageList.FirstOrDefault());
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
            Pagination.CurrentPage = SearchRequestModel.CurrentPage;
            Pagination.Count = SearchResultModel.Count;

            Pagination.CalculateTotalPage();
        }
        protected virtual void BeforeSearch()
        {
            SearchRequestModel.CurrentPage = Pagination.CurrentPage;
            SearchRequestModel.RequestItemSize = Pagination.PageSize;
        }

    }
}
