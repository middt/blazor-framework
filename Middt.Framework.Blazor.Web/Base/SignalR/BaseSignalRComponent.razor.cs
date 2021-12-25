using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.SignalR;
using System;

namespace Middt.Framework.Blazor.Web.Base.SignalR
{
    public partial class BaseSignalRComponent<TSignalRClient, TModel> : BaseComponent
        where TSignalRClient : BaseSignalRClient<TModel>
    {
        [Parameter]
        public RenderFragment Content { get; set; }

        [Inject]
        public TSignalRClient SignalRClient { get; set; }

        public Action<TModel> OnReceive { get; set; }

        public TModel Model { get; set; }

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                SignalRClient.OnReceive += (TModel model) =>
                {
                    DefaultValue(model);
                    OnReceive?.Invoke(model);
                };
            }
        }

        public void DefaultValue(TModel model)
        {
            Model = model;
        }
    }
}
