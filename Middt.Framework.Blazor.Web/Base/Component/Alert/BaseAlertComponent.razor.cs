using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Blazor.Web.Base.Component.Alert
{
    public partial class BaseAlertComponent : BaseComponent
    {
        [Parameter]
        public BaseResponseModel Result { get; set; }
    }
}
