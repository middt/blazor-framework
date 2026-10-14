using Middt.Framework.Common.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Code-behind base for a page hosting a <c>BaseDetailPage</c> referenced as <c>@ref="baseDetailPage"</c>.</summary>
public class BaseDetailPageCode<TService, TModel> : BasePageComponent, IPageHooks
    where TModel : BaseRequestModel, new()
    where TService : IListApiClient<TModel>
{
#pragma warning disable IDE1006 // referenced from page markup with this name
    public BaseDetailPageComponentClass<TService, TModel> baseDetailPage { get; set; } = default!;
#pragma warning restore IDE1006

    public TModel Model
    {
        get => baseDetailPage?.Model ?? new();
        set => baseDetailPage.Model = value;
    }

    public TService Service => baseDetailPage.Service;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && baseDetailPage is not null)
        {
            baseDetailPage.Hooks = this;
        }

        return base.OnAfterRenderAsync(firstRender);
    }

    public virtual Task Search() => baseDetailPage.Search();

    public override Task ExecuteMethod(Func<Task> action) => baseDetailPage.ExecuteMethod(action);

    public virtual Task OnBeforeSearch() => Task.CompletedTask;

    public virtual Task OnAfterSearch() => Task.CompletedTask;
}
