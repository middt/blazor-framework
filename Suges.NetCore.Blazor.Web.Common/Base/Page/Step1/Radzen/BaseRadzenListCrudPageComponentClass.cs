using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base
{
    public class BaseRadzenListCrudPageComponentClass<TService, TInterface, TModel> : BaseListCrudPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        public RadzenGrid<TModel> radzenGrid { get; set; }

        protected override void AfterSearch()
        {
            if (SearchRequestModel.RequestItemSize > 0)
            {
                radzenGrid.AllowPaging = true;
                radzenGrid.PageSize = SearchRequestModel.RequestItemSize;
                radzenGrid.Count = SearchResultModel.Count;

                InvokeAsync(() =>
                {
                    EventCallback<LoadDataArgs> callback = EventCallback.Factory.Create<LoadDataArgs>(this, arg =>
                    {
                        LoadData(arg);
                    });

                    radzenGrid.Data = SearchResultModel.Data;
                    radzenGrid.LoadData = callback;
                });

            }
            else
            {
                radzenGrid.AllowPaging = false;
                InvokeAsync(() =>
                {
                    radzenGrid.Data = SearchResultModel.Data;
                });
            }

            base.AfterSearch();
        }

        private async Task LoadData(LoadDataArgs args)
        {

            if (args.Top.HasValue && args.Top > 0)
            {
                SearchRequestModel.CurrentPage = (args.Skip.Value / args.Top.Value) + 1;
            }
            else
            {
                SearchRequestModel.CurrentPage = 1;
            }
            Search();

            //var query = dbContext.Employees.AsQueryable();

            //if (!string.IsNullOrEmpty(args.Filter))
            //{
            //    query = query.Where(args.Filter);
            //}

            //if (!string.IsNullOrEmpty(args.OrderBy))
            //{
            //    query = query.OrderBy(args.OrderBy);
            //}

            //employees = query.Skip(args.Skip.Value).Take(args.Top.Value).ToList();

            //count = dbContext.Employees.Count();

            // await InvokeAsync(StateHasChanged);
        }
    }
}
