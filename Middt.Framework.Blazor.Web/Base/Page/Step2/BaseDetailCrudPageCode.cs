using Middt.Framework.Common.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Code-behind base for a page hosting a <c>BaseDetailCrudPage</c> referenced as <c>@ref="baseDetailCrudPage"</c>.</summary>
public class BaseDetailCrudPageCode<TService, TModel> : BasePageComponent, IPageHooks
    where TModel : BaseRequestModel, new()
    where TService : ICrudApiClient<TModel>
{
#pragma warning disable IDE1006 // referenced from page markup with this name
    public BaseDetailCrudPageComponentClass<TService, TModel> baseDetailCrudPage { get; set; } = default!;
#pragma warning restore IDE1006

    public TModel Model
    {
        get => baseDetailCrudPage?.Model ?? new();
        set => baseDetailCrudPage.Model = value;
    }

    public TService Service => baseDetailCrudPage.Service;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && baseDetailCrudPage is not null)
        {
            baseDetailCrudPage.Hooks = this;
        }

        return base.OnAfterRenderAsync(firstRender);
    }

    public override Task ExecuteMethod(Func<Task> action) => baseDetailCrudPage.ExecuteMethod(action);

    public virtual Task Search() => baseDetailCrudPage.Search();

    public virtual Task SaveButtonClick() => baseDetailCrudPage.SaveButtonClick();

    public virtual Task Edit(TModel model) => baseDetailCrudPage.Edit(model);

    public virtual Task New() => baseDetailCrudPage.New();

    public virtual Task OnBeforeSearch() => Task.CompletedTask;

    public virtual Task OnAfterSearch() => Task.CompletedTask;
}
