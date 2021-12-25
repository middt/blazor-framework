using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Model.Model.Configuration;
using System;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.Security
{
    public partial class ReditectToLogin : ComponentBase
    {
        [Inject]
        public NavigationManager navigationManager { get; set; }

        [Inject]
        protected IBaseConfiguration baseConfiguration { get; set; }

        protected override Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                SecuritySettings securitySettings = baseConfiguration.Get<SecuritySettings>();
                navigationManager.NavigateTo(securitySettings.LoginPageURL + $"?returnUrl={Uri.EscapeDataString(navigationManager.ToBaseRelativePath(navigationManager.Uri))}");
            }
            return base.OnAfterRenderAsync(firstRender);
        }
    }
}
