using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Blazor.Web.Extensions;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Loads a single record by id (from the <see cref="id"/> parameter or the <see cref="QueryStringID"/> query string key).</summary>
public class BaseDetailPageComponentClass<TService, TModel> : BasePageComponent
    where TModel : BaseRequestModel, new()
    where TService : IListApiClient<TModel>
{
    [Parameter]
    public RenderFragment? DetailContent { get; set; }

    [Parameter]
    public string? QueryStringID { get; set; }

#pragma warning disable IDE1006 // public parameter name kept for compatibility
    [Parameter]
    public int? id { get; set; }
#pragma warning restore IDE1006

    /// <summary>Custom load call; defaults to <see cref="IListApiClient{TModel}.GetByIdAsync"/>.</summary>
    [Parameter]
    public Func<int, CancellationToken, Task<BaseResponseDataModel<TModel>>>? GetFunc { get; set; }

    [Parameter]
    public bool IsFirstLoad { get; set; } = true;

    [Parameter]
    public EventCallback OnAfterSearch { get; set; }

    [Parameter]
    public EventCallback OnBeforeSearch { get; set; }

    [Inject]
    public TService Service { get; set; } = default!;

    public IPageHooks? Hooks { get; set; }

    public TModel? Model { get; set; }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        if (!firstRender)
        {
            return;
        }

        if (id is null && !string.IsNullOrEmpty(QueryStringID) &&
            int.TryParse(NavigationManager.QueryString(QueryStringID), NumberStyles.Integer, CultureInfo.InvariantCulture, out var queryId))
        {
            id = queryId;
        }

        if (IsFirstLoad)
        {
            await Search();
        }
    }

    public virtual Task Search() => ExecuteMethod(async () =>
    {
        await BeforeSearch();

        if (id is { } recordId)
        {
            var get = GetFunc ?? Service.GetByIdAsync;
            var result = await get(recordId, ComponentCancellation);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("Load failed: {Error}", result.ErrorText);
                Notification.ShowErrorMessage("Kayıt yüklenirken bir hata oluştu.", result.MessageList.FirstOrDefault());
            }
            else if (result.Data is null)
            {
                Notification.ShowInfoMessage("Bilgi", "Kayıt bulunamadı.");
            }
            else
            {
                Model = result.Data;
            }
        }

        await AfterSearch();
    });

    public virtual async Task BeforeSearch()
    {
        if (Hooks is not null)
        {
            await Hooks.OnBeforeSearch();
        }

        await OnBeforeSearch.InvokeAsync();
    }

    protected virtual async Task AfterSearch()
    {
        if (Hooks is not null)
        {
            await Hooks.OnAfterSearch();
        }

        await OnAfterSearch.InvokeAsync();
    }
}
