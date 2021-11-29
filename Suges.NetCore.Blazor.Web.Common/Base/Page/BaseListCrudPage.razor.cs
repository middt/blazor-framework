using Microsoft.AspNetCore.Components;
using Radzen;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Blazor.Web.Base.Component.Pagination;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public partial class BaseListCrudPage<TService, TInterface, TModel> : BaseListCrudPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        public BasePaginationComponent Pagination { get; set; }

        protected override void AfterSearch()
        {
            base.AfterSearch();

            Pagination.PageSize = SearchRequestModel.RequestItemSize;
            Pagination.CurrentPage = SearchRequestModel.CurrentPage;
            Pagination.Count = SearchResultModel.Count;

            Pagination.CalculateTotalPage();
        }






        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                Pagination.OnPageChange += (int page) =>
                {
                    GotoPage(page).Wait();
                };
            }
        }

        public virtual async Task GotoPage(int Page)
        {
            if (Page > 0)
            {
                SearchRequestModel.CurrentPage = Page;
                Search();
            }
        }
    }
}
