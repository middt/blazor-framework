using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Component.Security
{
    public partial class BaseAuthorizeView: Middt.Framework.Blazor.Web.Base.BaseAuthorizeComponent
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
