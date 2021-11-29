using Microsoft.AspNetCore.Components;

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
