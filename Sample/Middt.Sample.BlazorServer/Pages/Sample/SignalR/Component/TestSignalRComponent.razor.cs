using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base;
using Middt.Framework.Blazor.Web.Base.SignalR;
using Middt.Sample.BlazorServer.SignalR;
using System;
using System.Threading.Tasks;

namespace Middt.Sample.BlazorServer.Pages.Sample.SignalR.Component
{
    public partial class TestSignalRComponent : BaseComponent
    {
        public BaseSignalRComponent<TestSignalRClient, string> testSignalRComponent { get; set; }

        public String Model { get { return testSignalRComponent.Model; } }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if(firstRender)
            {
                testSignalRComponent.OnReceive += OnReceive;
            }
        }
        protected void OnReceive(string message)
        {
            InvokeAsync(()=>StateHasChanged());
        }
        public void Dispose()
        {
            testSignalRComponent.OnReceive -= OnReceive;
        }
        ~TestSignalRComponent()
        {
            testSignalRComponent.OnReceive -= OnReceive;
        }
         
    }
}
