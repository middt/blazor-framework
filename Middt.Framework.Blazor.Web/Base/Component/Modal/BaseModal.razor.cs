using Microsoft.AspNetCore.Components;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal;

/// <summary>
/// Shows <see cref="BaseModalCode.ChildContent"/> in a Radzen dialog. <see cref="BaseModalCode.Open"/> returns as soon
/// as the dialog is shown; <see cref="BaseModalCode.OnClose"/> fires when it is closed (by code or by the user).
/// </summary>
public partial class BaseModal : BaseModalCode
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    protected override Task OpenModal()
    {
        var options = new DialogOptions
        {
            ShowClose = ShowClose,
            ShowTitle = ShowTitle,
            Style = Style,
            Width = IsFullScreen ? "90%" : Width,
            Height = IsFullScreen ? "90%" : Height,
        };

        // OpenAsync completes when the dialog closes; observe it without blocking the caller.
        _ = WaitForCloseAsync(DialogService.OpenAsync(Title ?? string.Empty, _ => ChildContent ?? (_ => { }), options));
        return Task.CompletedTask;
    }

    protected override Task CloseModal()
    {
        DialogService.Close();
        return Task.CompletedTask;
    }

    /// <summary>Re-renders the dialog content after the model behind it changed.</summary>
    public void Refresh() => DialogService.Refresh();

    private async Task WaitForCloseAsync(Task<dynamic?> dialog)
    {
        try
        {
            await dialog;
        }
        finally
        {
            IsModalOpen = false;
            await OnClose.InvokeAsync(ModalName);
        }
    }
}
