using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Code-behind base for a page hosting a <c>BaseListCrudPage</c>/<c>BaseRadzenListCrudPage</c> referenced as <c>@ref="baseListCrudPage"</c>.</summary>
public class BaseListCrudPageCode<TService, TModel> : BasePageComponent, ICrudPageHooks
    where TModel : BaseRequestModel, new()
    where TService : ICrudApiClient<TModel>
{
#pragma warning disable IDE1006 // referenced from page markup with this name
    public BaseListCrudPageComponentClass<TService, TModel> baseListCrudPage { get; set; } = default!;
#pragma warning restore IDE1006

    public TModel Model
    {
        get => baseListCrudPage?.Model ?? new();
        set => baseListCrudPage.Model = value;
    }

    public BaseSearchRequestModel<TModel> SearchRequestModel
    {
        get => baseListCrudPage?.SearchRequestModel ?? new();
        set => baseListCrudPage.SearchRequestModel = value;
    }

    public BaseResponseDataModel<List<TModel>> SearchResultModel
    {
        get => baseListCrudPage.SearchResultModel;
        set => baseListCrudPage.SearchResultModel = value;
    }

    public TService Service => baseListCrudPage.Service;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && baseListCrudPage is not null)
        {
            baseListCrudPage.Hooks = this;
        }

        return base.OnAfterRenderAsync(firstRender);
    }

    public virtual Task Edit(TModel model) => baseListCrudPage.Edit(model);

    public virtual Task Delete(TModel model) => baseListCrudPage.Delete(model);

    public virtual Task Search() => baseListCrudPage.Search();

    public override Task ExecuteMethod(Func<Task> action) => baseListCrudPage.ExecuteMethod(action);

    public virtual Task OnBeforeSearch() => Task.CompletedTask;

    public virtual Task OnAfterSearch() => Task.CompletedTask;

    public virtual Task OnBeforeModalOpen() => Task.CompletedTask;

    public virtual Task OnAfterModalClose() => Task.CompletedTask;
}
