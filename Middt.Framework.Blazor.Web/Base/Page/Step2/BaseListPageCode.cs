using Middt.Framework.Common.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>
/// Code-behind base for a routable page that hosts a <c>BaseListPage</c>/<c>BaseRadzenListPage</c>
/// referenced as <c>@ref="baseListPage"</c>; override <see cref="OnBeforeSearch"/>/<see cref="OnAfterSearch"/> to customise.
/// </summary>
public class BaseListPageCode<TService, TModel> : BasePageComponent, IPageHooks
    where TModel : BaseRequestModel, new()
    where TService : IListApiClient<TModel>
{
#pragma warning disable IDE1006 // referenced from page markup with this name
    public BaseListPageComponentClass<TService, TModel> baseListPage { get; set; } = default!;
#pragma warning restore IDE1006

    public BaseSearchRequestModel<TModel> SearchRequestModel
    {
        get => baseListPage?.SearchRequestModel ?? new();
        set => baseListPage.SearchRequestModel = value;
    }

    public BaseResponseDataModel<List<TModel>> SearchResultModel
    {
        get => baseListPage.SearchResultModel;
        set => baseListPage.SearchResultModel = value;
    }

    public TService Service => baseListPage.Service;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        // Registered synchronously so the hooks are in place before the child page's first search completes.
        if (firstRender && baseListPage is not null)
        {
            baseListPage.Hooks = this;
        }

        return base.OnAfterRenderAsync(firstRender);
    }

    public virtual Task Search() => baseListPage.Search();

    public override Task ExecuteMethod(Func<Task> action) => baseListPage.ExecuteMethod(action);

    public virtual Task OnBeforeSearch() => Task.CompletedTask;

    public virtual Task OnAfterSearch() => Task.CompletedTask;
}
