using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Component.Security;

/// <summary>Renders <see cref="Authorized"/> or <see cref="NotAuthorized"/> once the client token has been loaded.</summary>
public partial class BaseAuthorizeView : BaseAuthorizeComponent
{
    [Parameter]
    public RenderFragment? Authorized { get; set; }

    [Parameter]
    public RenderFragment? NotAuthorized { get; set; }
}
