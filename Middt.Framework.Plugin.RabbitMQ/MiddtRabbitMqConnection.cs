using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Middt.Framework.Plugin.RabbitMQ;

/// <summary>
/// Base class owning a RabbitMQ connection/channel and declaring the exchange topology.
/// No I/O happens in the constructor; call <see cref="EnsureInitializedAsync"/> before using <see cref="Channel"/>.
/// </summary>
public abstract class MiddtRabbitMqConnection : IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ExchangeTypes _exchangeType;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    protected MiddtRabbitMqConnection(IConnectionFactory connectionFactory, ExchangeTypes exchangeType, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _exchangeType = exchangeType;
        Logger = logger;
    }

    /// <summary>Prefix applied to exchange, queue and routing-key names.</summary>
    protected virtual string VirtualHost => "/";

    protected abstract string Exchange { get; }

    protected abstract string Queue { get; }

    protected abstract string AppId { get; }

    protected abstract string QueueAndExchangeRoutingKey { get; }

    protected ILogger Logger { get; }

    protected internal string RefinedExchangeName => $"{VirtualHost}.{Exchange}";

    protected internal string RefinedQueueName => $"{VirtualHost}.{Queue}";

    protected internal string RefinedRoutingKey => $"{VirtualHost}.{QueueAndExchangeRoutingKey}";

    /// <summary>The open channel. Only valid after <see cref="EnsureInitializedAsync"/> completed.</summary>
    protected IChannel Channel =>
        _channel ?? throw new InvalidOperationException($"{GetType().Name} is not initialized. Call {nameof(EnsureInitializedAsync)} first.");

    /// <summary>Opens the connection and channel (re-opening them if closed) and declares the topology.</summary>
    protected async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_channel is { IsOpen: true })
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return;
            }

            if (_connection is not { IsOpen: true })
            {
                if (_connection is not null)
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }

                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_channel is not null)
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await DeclareTopologyAsync(_channel, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Declares the exchange. Consumers override this to also declare and bind their queue.</summary>
    protected virtual Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.ExchangeDeclareAsync(
            exchange: RefinedExchangeName,
            type: ToRabbitExchangeType(_exchangeType),
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        _initLock.Dispose();
    }

    private static string ToRabbitExchangeType(ExchangeTypes exchangeType) => exchangeType switch
    {
        ExchangeTypes.Direct => ExchangeType.Direct,
        ExchangeTypes.Fanout => ExchangeType.Fanout,
        ExchangeTypes.Topic => ExchangeType.Topic,
        ExchangeTypes.Headers => ExchangeType.Headers,
        _ => throw new ArgumentOutOfRangeException(nameof(exchangeType), exchangeType, null),
    };
}
