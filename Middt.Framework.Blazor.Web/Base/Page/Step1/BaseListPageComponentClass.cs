using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Blazor.Web.Base.Component.Pagination;
using Middt.Framework.Common.Http;
using Middt.Framework.Common.Model.Data;
using Radzen;
using Radzen.Blazor;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>
/// Search + paged list over an <see cref="IListApiClient{TModel}"/>. Works with the framework pager
/// (<see cref="Pagination"/>) or a Radzen data grid (<see cref="DataGrid"/>) for server side paging and sorting.
/// </summary>
public class BaseListPageComponentClass<TService, TModel> : BasePageComponent
    where TModel : BaseRequestModel, new()
    where TService : IListApiClient<TModel>
{
    [Parameter]
    public int PageSize { get; set; } = 20;

    [Parameter]
    public bool HideSearchButton { get; set; }

    [Parameter]
    public bool HideSearch { get; set; }

    [Parameter]
    public RenderFragment? SearchContent { get; set; }

    [Parameter]
    public RenderFragment? ListContent { get; set; }

    /// <summary>Loads data on first render. Set to <c>false</c> when the page triggers <see cref="Search"/> itself.</summary>
    [Parameter]
    public bool IsFirstLoad { get; set; } = true;

    /// <summary>Custom search call; defaults to <see cref="IListApiClient{TModel}.GetItemsAsync"/>.</summary>
    [Parameter]
    public Func<BaseSearchRequestModel<TModel>, CancellationToken, Task<BaseResponseDataModel<List<TModel>>>>? SearchFunc { get; set; }

    [Parameter]
    public EventCallback OnAfterSearch { get; set; }

    [Parameter]
    public EventCallback OnBeforeSearch { get; set; }

    [Inject]
    public TService Service { get; set; } = default!;

    /// <summary>Code-behind callbacks registered by the Step2 page classes.</summary>
    public IPageHooks? Hooks { get; set; }

    public BaseSearchRequestModel<TModel> SearchRequestModel { get; set; } = new();

    public BaseResponseDataModel<List<TModel>> SearchResultModel { get; set; } = BaseResponseDataModel<List<TModel>>.Success([]);

    public BasePaginationComponent? Pagination { get; set; }

    public RadzenDataGrid<TModel>? DataGrid { get; set; }

    protected bool IsSearching { get; private set; }

    protected override void OnInitialized() => SearchRequestModel.RequestItemSize = PageSize;

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        if (firstRender && IsFirstLoad)
        {
            await Search();
        }
    }

    public virtual Task GotoPage() => Search();

    protected virtual async Task SearchButton()
    {
        SearchRequestModel.CurrentPage = 1;
        if (Pagination is not null)
        {
            Pagination.CurrentPage = 1;
        }

        if (DataGrid is not null)
        {
            // Resets the grid to page 1; the grid then calls LoadData → Search.
            await DataGrid.FirstPage(true);
            return;
        }

        await Search();
    }

    public virtual Task Search() => ExecuteMethod(async () =>
    {
        IsSearching = true;
        try
        {
            await BeforeSearch();

            var search = SearchFunc ?? Service.GetItemsAsync;
            SearchResultModel = await search(SearchRequestModel, ComponentCancellation);

            if (!SearchResultModel.IsSuccess)
            {
                Logger.LogWarning("Search failed: {Error}", SearchResultModel.ErrorText);
                Notification.ShowErrorMessage("Arama yapılırken bir hata oluştu.", SearchResultModel.MessageList.FirstOrDefault());
            }
            else if (SearchResultModel.Data is not { Count: > 0 })
            {
                Notification.ShowInfoMessage("Bilgi", "Kayıt bulunamadı.");
            }

            await AfterSearch();
        }
        finally
        {
            IsSearching = false;
        }
    });

    public virtual Task Cancel()
    {
        SearchRequestModel = new BaseSearchRequestModel<TModel> { RequestItemSize = SearchRequestModel.RequestItemSize };
        return Task.CompletedTask;
    }

    public virtual async Task BeforeSearch()
    {
        if (Pagination is not null)
        {
            SearchRequestModel.CurrentPage = Pagination.CurrentPage;
            SearchRequestModel.RequestItemSize = Pagination.PageSize;
        }

        if (Hooks is not null)
        {
            await Hooks.OnBeforeSearch();
        }

        await OnBeforeSearch.InvokeAsync();
    }

    protected virtual async Task AfterSearch()
    {
        if (Pagination is not null)
        {
            Pagination.CurrentPage = SearchRequestModel.CurrentPage;
            Pagination.Count = SearchResultModel.Count;
            await Pagination.CalculateTotalPage();
        }

        if (Hooks is not null)
        {
            await Hooks.OnAfterSearch();
        }

        await OnAfterSearch.InvokeAsync();
    }

    /// <summary>Radzen grid callback for server side paging/sorting.</summary>
    protected async Task OnGridLoadData(LoadDataArgs args)
    {
        // The grid asks for data while initialising (also during prerendering); the first load is driven by
        // CustomOnAfterRenderAsync instead so the API is called once.
        if (!IsFirstRenderCompleted || IsSearching)
        {
            return;
        }

        if (args.Top is > 0)
        {
            SearchRequestModel.RequestItemSize = args.Top.Value;
            SearchRequestModel.CurrentPage = ((args.Skip ?? 0) / args.Top.Value) + 1;
        }

        var sort = args.Sorts?.FirstOrDefault(s => s.SortOrder is not null);
        SearchRequestModel.SortProperty = sort?.Property;
        SearchRequestModel.SortReverse = sort?.SortOrder == SortOrder.Ascending;

        await Search();
    }
}
