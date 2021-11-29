using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base;
using Middt.Framework.Blazor.Web.Base.SignalR;
using Middt.Sample.BlazorServer.SignalR;
using System;

namespace Middt.Sample.BlazorServer.Pages.Sample.SignalR.Component
{
    public partial class TestSignalRComponent : BaseComponent
    {
        [Parameter]
        public RenderFragment Content { get; set; }

        public BaseSignalRComponent<TestSignalRClient, string> testSignalRComponent { get; set; }

        public String Model { get { return testSignalRComponent.Model; } }
    }
}
