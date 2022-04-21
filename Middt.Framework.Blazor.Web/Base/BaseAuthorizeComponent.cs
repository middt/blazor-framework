using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base
{
    public class BaseAuthorizeComponent : BaseComponent
    {
        [Parameter]
        public List<string> Roles { get; set; }


        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

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
