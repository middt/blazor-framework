using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.SignalR;
using System;
using System.Threading.Tasks;

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

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);


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
