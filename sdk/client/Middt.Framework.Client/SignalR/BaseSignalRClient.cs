using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.SignalR;

namespace Middt.Framework.Client.SignalR;

/// <summary>
/// SignalR client for a <see cref="BaseSignalRHub{TModel}"/>. The connection is created lazily by
/// <see cref="StartAsync"/> (not in the constructor) and closed by <see cref="DisposeAsync"/>.
/// </summary>
public abstract class BaseSignalRClient<TModel> : IBaseSignalRClient<TModel>, IAsyncDisposable
{
    private readonly HubConnection connection;
    private readonly ILogger logger;
    private readonly SemaphoreSlim startLock = new(1, 1);

    protected BaseSignalRClient(IOptions<ApiSettings> apiSettings, IBaseSessionState sessionState, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(apiSettings);
        this.logger = logger;

        var hubUrl = new Uri(new Uri(apiSettings.Value.URL.TrimEnd('/') + "/"), HubName.TrimStart('/'));

        connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options => options.AccessTokenProvider = async () => await sessionState.Token())
            .WithAutomaticReconnect()
            .Build();

        connection.On<TModel>(nameof(Receive), Receive);
    }

    /// <summary>Relative hub path, e.g. <c>/hubs/testhub</c>.</summary>
    public abstract string HubName { get; }

    public event Action<TModel>? OnReceive;

    public HubConnectionState State => connection.State;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await startLock.WaitAsync(cancellationToken);
        try
        {
            if (connection.State == HubConnectionState.Disconnected)
            {
                await connection.StartAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(ex, "SignalR connection to {Hub} could not be started", HubName);
        }
        finally
        {
            startLock.Release();
        }
    }

    public Task SendAsync(string methodName, object? argument, CancellationToken cancellationToken = default) =>
        connection.InvokeAsync(methodName, argument, cancellationToken);

    public Task Receive(TModel model)
    {
        OnReceive?.Invoke(model);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        startLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
