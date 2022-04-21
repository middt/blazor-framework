using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Pagination;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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


        [Parameter]
        public EventCallback? OnAfterSearch { get; set; }

        [Parameter]
        public EventCallback? OnBeforeSearch { get; set; }



        public BasePaginationComponent Pagination { get; set; }

        public BaseListPageComponentClass()
        {
            SearchRequestModel = new BaseSearchRequestModel<TModel>();
        }
        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                Pagination.OnPageChange = EventCallback.Factory.Create(this, GotoPage);


                if (IsFirstLoad)
                {
                    await Search();
                }
            }
        }
        public virtual async Task GotoPage()
        {
            await Search();
        }

        protected async Task SearchButton()
        {
            Pagination.CurrentPage = 1;
            await Search();
        }
        public virtual async Task Search()
        {
            await ExecuteMethod(async () =>
            {
                await InvokeAsync(async () =>
                {
                    await BeforeSearch();
                });

                Type serviceType = Service.GetType();
                MethodInfo searchMethod = serviceType.GetMethods().Single(
                        mi => mi.Name == SearchMethod && 
                        mi.GetParameters().Length == 1);

                SearchResultModel = await (Task<BaseResponseDataModel<List<TModel>>>)searchMethod.Invoke(Service, new object[] { SearchRequestModel });

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

                await InvokeAsync(async () =>
                {
                    await AfterSearch();
                });
            });
        }

        public virtual async Task Cancel()
        {
            SearchRequestModel = new BaseSearchRequestModel<TModel>();
        }
        protected virtual async Task AfterSearch()
        {
            Pagination.CurrentPage = SearchRequestModel.CurrentPage;
            Pagination.Count = SearchResultModel.Count;

            await Pagination.CalculateTotalPage();

            if (OnAfterSearch != null)
                await OnAfterSearch?.InvokeAsync();
        }
        public virtual async Task BeforeSearch()
        {
            SearchRequestModel.CurrentPage = Pagination.CurrentPage;
            SearchRequestModel.RequestItemSize = Pagination.PageSize;

            if (OnBeforeSearch != null)
                await OnBeforeSearch?.InvokeAsync();
        }

    }
}
