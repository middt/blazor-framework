namespace Middt.Framework.Blazor.Web.Base.Component.Modal;

/// <summary>Full screen loading overlay used by <see cref="BaseComponent.ExecuteMethod"/>.</summary>
public partial class BaseLoadingModal : BaseModalCode
{
    protected override Task OpenModal() => InvokeAsync(StateHasChanged);

    protected override Task CloseModal() => InvokeAsync(StateHasChanged);
}
