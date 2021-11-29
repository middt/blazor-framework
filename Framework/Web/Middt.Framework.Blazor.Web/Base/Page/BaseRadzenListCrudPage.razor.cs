using Radzen;
using Radzen.Blazor;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Page
{
    public partial class BaseRadzenListCrudPage<TService, TInterface, TModel> : BaseListCrudPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        public RadzenGrid<TModel> radzenGrid { get; set; }

        protected override void AfterSearch()
        {
            if (SearchRequestModel.RequestItemSize > 0)
            {
                InvokeAsync(() =>
                {
                    radzenGrid.Data = SearchResultModel.Data;
                });

            }
            else
            {
                radzenGrid.Data = SearchResultModel.Data;
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
