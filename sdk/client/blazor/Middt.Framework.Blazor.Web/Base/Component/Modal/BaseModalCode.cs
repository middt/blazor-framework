using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal;

public abstract class BaseModalCode : BaseComponent
{
    [Parameter]
    public bool ShowTitle { get; set; } = true;

    [Parameter]
    public bool ShowClose { get; set; } = true;

    [Parameter]
    public string? Style { get; set; }

    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public RenderFragment? FooterContent { get; set; }

    [Parameter]
    public string? ModalName { get; set; }

    [Parameter]
    public EventCallback<string> OnClose { get; set; }

    [Parameter]
    public bool IsFullScreen { get; set; }

    [Parameter]
    public string? Width { get; set; }

    [Parameter]
    public string? Height { get; set; }

    public bool IsModalOpen { get; protected set; }

    public virtual async Task Open()
    {
        IsModalOpen = true;
        await OpenModal();
    }

    public virtual async Task Close()
    {
        IsModalOpen = false;
        await CloseModal();
    }
}
