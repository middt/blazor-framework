using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Common.Http;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Detail page with an edit form: updates an existing record or inserts a new one when no id is given.</summary>
public class BaseDetailCrudPageComponentClass<TService, TModel> : BaseDetailPageComponentClass<TService, TModel>
    where TModel : BaseRequestModel, new()
    where TService : ICrudApiClient<TModel>
{
    /// <summary>Navigates here after a successful save (otherwise a notification is shown).</summary>
    [Parameter]
    public string? SaveAfterURL { get; set; }

    protected bool IsUpdate { get; set; }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            IsUpdate = Model is not null;
            if (Model is null)
            {
                await New();
            }
        }
    }

    public Task New() => Open(new TModel(), isUpdate: false);

    public Task Edit(TModel model) => Open(model, isUpdate: true);

    private Task Open(TModel model, bool isUpdate)
    {
        IsUpdate = isUpdate;
        Model = (TModel)model.Clone();
        StateHasChanged();
        return Task.CompletedTask;
    }

    public Task SaveButtonClick() => ExecuteMethod(async () =>
    {
        var result = IsUpdate
            ? await Service.UpdateAsync(Model!, ComponentCancellation)
            : await Service.InsertAsync(Model!, ComponentCancellation);

        if (!result.IsSuccess)
        {
            Logger.LogWarning("Save failed: {Error}", result.ErrorText);
            Notification.ShowErrorMessage("Bilgiler kaydedilirken bir hata oluştu.", result.MessageList.FirstOrDefault());
            return;
        }

        if (!string.IsNullOrEmpty(SaveAfterURL))
        {
            NavigationManager.NavigateTo(SaveAfterURL);
        }
        else
        {
            Model = result.Data ?? Model;
            IsUpdate = true;
            Notification.ShowSuccessMessage("Bilgiler başarı ile kaydedildi.", null);
        }
    });
}
