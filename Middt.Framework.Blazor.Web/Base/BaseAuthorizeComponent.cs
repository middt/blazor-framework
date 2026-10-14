using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base;

/// <summary>Sets <see cref="BaseComponent.IsAuthenticated"/> from the token, optionally requiring one of <see cref="Roles"/>.</summary>
public class BaseAuthorizeComponent : BaseComponent
{
    [Parameter]
    public IReadOnlyCollection<string>? Roles { get; set; }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        var authenticated = BaseTokenHelper.IsAuthenticated && (Roles is not { Count: > 0 } || BaseTokenHelper.IsHasRoles(Roles));
        if (authenticated != IsAuthenticated)
        {
            IsAuthenticated = authenticated;
            StateHasChanged();
        }
    }
}
