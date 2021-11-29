using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Suges.Framework.Blazor.Web.Base;
using Suges.Framework.Common.Dependency;
using Suges.Framework.Common.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.Security
{
    public partial class BaseAuthorizeView : BaseAuthorizeComponent
    {
   
        [Parameter]
        public RenderFragment Authorized { get; set; }
        [Parameter]
        public RenderFragment NotAuthorized { get; set; }

        public RenderFragment Display { get; set; }

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            //if(firstRender)
            //{
                if (IsAuthenticated)
                    Display = Authorized;
                else
                    Display = NotAuthorized;
            //}
        }
    }
}
