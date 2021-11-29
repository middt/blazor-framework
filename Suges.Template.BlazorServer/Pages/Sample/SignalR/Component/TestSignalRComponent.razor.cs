using Microsoft.AspNetCore.Components;
using Suges.Framework.Blazor.Web.Base;
using Suges.Framework.Blazor.Web.Base.SignalR;
using Suges.Template.BlazorServer.SignalR;
using System;

namespace Suges.Template.BlazorServer.Pages.Sample.SignalR.Component
{
    public partial class TestSignalRComponent : BaseComponent
    {
        [Parameter]
        public RenderFragment Content { get; set; }

        public BaseSignalRComponent<TestSignalRClient, string> testSignalRComponent { get; set; }

        public String Model { get { return testSignalRComponent.Model; } }
    }
}
