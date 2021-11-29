using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Notification;
using Suges.Framework.Common.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base
{
    public class BaseAuthorizeComponent : BaseComponent
    {
        [Parameter]
        public List<string> Roles { get; set; }


        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            //if (firstRender)
            //{
                if (baseTokenHelper.claimsIdentity.IsAuthenticated)
                {
                    IsAuthenticated = baseTokenHelper.claimsIdentity.IsAuthenticated;
                    if (Roles != null && Roles.Count > 0)
                    {
                        IsAuthenticated = baseTokenHelper.IsHasRoles(Roles);
                    }
                }
            //}
        }

    }
}
