using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.SignalR;

namespace Middt.Framework.Blazor.Web.Base.SignalR;

/// <summary>Connects <typeparamref name="TSignalRClient"/> after the first render and re-renders on every received message.</summary>
public partial class BaseSignalRComponent<TSignalRClient, TModel> : BaseComponent
    where TSignalRClient : BaseSignalRClient<TModel>
{
    [Parameter]
    public RenderFragment? Content { get; set; }

    [Parameter]
    public EventCallback<TModel> OnMessage { get; set; }

    [Inject]
    public TSignalRClient SignalRClient { get; set; } = default!;

    /// <summary>Raised for every message (after <see cref="Model"/> was updated).</summary>
    public event Action<TModel>? OnReceive;

    public TModel? Model { get; private set; }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            SignalRClient.OnReceive += HandleReceive;
            await SignalRClient.StartAsync(ComponentCancellation);
        }
    }

    private void HandleReceive(TModel model) => _ = InvokeAsync(async () =>
    {
        Model = model;
        OnReceive?.Invoke(model);
        await OnMessage.InvokeAsync(model);
        StateHasChanged();
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing && SignalRClient is not null)
        {
            SignalRClient.OnReceive -= HandleReceive;
        }

        base.Dispose(disposing);
    }
}
