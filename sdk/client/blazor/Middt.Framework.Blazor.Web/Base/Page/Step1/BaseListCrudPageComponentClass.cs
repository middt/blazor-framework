using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>List page with a modal editor for insert/update and confirmed delete.</summary>
public class BaseListCrudPageComponentClass<TService, TModel> : BaseListPageComponentClass<TService, TModel>
    where TModel : BaseRequestModel, new()
    where TService : ICrudApiClient<TModel>
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Parameter]
    public RenderFragment? EditContent { get; set; }

    [Parameter]
    public string? ModalTitle { get; set; }

    /// <summary>Property values applied to every model opened in the editor (e.g. a fixed foreign key).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, object?>? ModalParameters { get; set; }

    [Parameter]
    public bool IsCloseModalAfterEdit { get; set; } = true;

    [Parameter]
    public bool IsModalFullScreen { get; set; }

    [Parameter]
    public string? ModalWidth { get; set; }

    [Parameter]
    public string? ModalHeight { get; set; }

    [Parameter]
    public bool IsLoadAfterSave { get; set; } = true;

    [Parameter]
    public bool HideInsertButton { get; set; }

    [Parameter]
    public string InsertText { get; set; } = "Yeni Kayıt";

    [Parameter]
    public EventCallback<TModel> OnAfterSave { get; set; }

    [Parameter]
    public EventCallback OnAfterModalClose { get; set; }

    [Parameter]
    public EventCallback OnBeforeModalOpen { get; set; }

    public TModel? Model { get; set; }

    public BaseCrudModal? CrudModal { get; set; }

    protected bool IsUpdate { get; set; }

    public Task New() => Open(new TModel(), isUpdate: false);

    public Task Edit(TModel model) => Open(model, isUpdate: true);

    private async Task Open(TModel model, bool isUpdate)
    {
        IsUpdate = isUpdate;
        Model = (TModel)model.Clone();

        foreach (var (name, value) in ModalParameters ?? new Dictionary<string, object?>())
        {
            typeof(TModel).GetProperty(name)?.SetValue(Model, value);
        }

        if (Hooks is ICrudPageHooks crudHooks)
        {
            await crudHooks.OnBeforeModalOpen();
        }

        await OnBeforeModalOpen.InvokeAsync();
        await CrudModal!.Open(Model);
        StateHasChanged();
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

        Notification.ShowSuccessMessage(IsUpdate ? "Bilgiler başarı ile güncellendi." : "Bilgiler başarı ile kaydedildi.", null);

        if (IsCloseModalAfterEdit && CrudModal is not null)
        {
            await CrudModal.Close();
        }

        if (IsLoadAfterSave)
        {
            await Search();
        }

        await OnAfterSave.InvokeAsync(result.Data);
    });

    protected virtual async Task CloseButtonClick()
    {
        if (Hooks is ICrudPageHooks crudHooks)
        {
            await crudHooks.OnAfterModalClose();
        }

        await OnAfterModalClose.InvokeAsync();
    }

    public async Task Delete(TModel model)
    {
        var confirmed = await DialogService.Confirm(
            "Kayıt silinecek. Emin misiniz?",
            "Sil",
            new ConfirmOptions { OkButtonText = "Evet", CancelButtonText = "Hayır" });

        if (confirmed != true)
        {
            return;
        }

        await ExecuteMethod(async () =>
        {
            var result = await Service.DeleteAsync(model, ComponentCancellation);
            if (!result.IsSuccess)
            {
                Notification.ShowErrorMessage("Kayıt silinemedi.", result.MessageList.FirstOrDefault());
                return;
            }

            Notification.ShowSuccessMessage("Kayıt silindi.", null);
            await Search();
        });
    }
}
