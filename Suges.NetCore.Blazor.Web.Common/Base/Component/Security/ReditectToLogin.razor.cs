using Microsoft.AspNetCore.Components;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Dependency;
using Suges.Framework.Common.Security;
using Suges.Framework.Model.Model.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.Security
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
