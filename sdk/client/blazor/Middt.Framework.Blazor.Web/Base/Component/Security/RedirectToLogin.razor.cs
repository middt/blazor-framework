using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Web.Extensions;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Blazor.Web.Base.Component.Security;

public partial class RedirectToLogin : ComponentBase
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IOptions<SecuritySettings> SecuritySettings { get; set; } = default!;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            NavigationManager.NavigateToLogin(SecuritySettings.Value.LoginPageURL);
        }
    }
}
