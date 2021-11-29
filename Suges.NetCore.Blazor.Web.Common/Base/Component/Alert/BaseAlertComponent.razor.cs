using Microsoft.AspNetCore.Components;
using Suges.Framework.Common.Model.Data;

namespace Suges.Framework.Blazor.Web.Base.Component.Alert
{
    public partial class BaseAlertComponent : BaseComponent
    {
        [Parameter]
        public BaseResponseModel Result { get; set; }
    }
}
