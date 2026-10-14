using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal;

/// <summary>Dialog with an <c>EditForm</c> (data annotations validation) and Save/Cancel buttons.</summary>
public partial class BaseCrudModal : BaseModalCode
{
    protected BaseModal? BaseModalRef { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    public object? Model { get; private set; }

    public async Task Open(object model)
    {
        Model = model;
        IsModalOpen = true;
        await BaseModalRef!.Open();
    }

    public override Task Close() => BaseModalRef?.Close() ?? Task.CompletedTask;

    private Task Save() => OnSave.InvokeAsync();

    private Task HandleClosed(string _)
    {
        IsModalOpen = false;
        return OnClose.InvokeAsync(ModalName);
    }
}
