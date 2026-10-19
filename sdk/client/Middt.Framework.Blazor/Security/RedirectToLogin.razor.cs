using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Extensions;
using Middt.Framework.Model.Configuration;

namespace Middt.Framework.Blazor.Security;

/// <summary>
/// <c>NotAuthorized</c> content of the router: anonymous users go to the login page, signed-in users without the
/// required role see "Erişim engellendi". Sending them to the login page instead would loop with an identity provider:
/// the login page signs them in again silently (single sign-on) and returns to the same forbidden page.
/// </summary>
public partial class RedirectToLogin : ComponentBase
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IOptions<SecuritySettings> SecuritySettings { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private bool denied;

    protected override async Task OnInitializedAsync()
    {
        denied = AuthenticationState is not null && (await AuthenticationState).User.Identity?.IsAuthenticated == true;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !denied)
        {
            NavigationManager.NavigateToLogin(SecuritySettings.Value.LoginPageURL);
        }
    }
}
