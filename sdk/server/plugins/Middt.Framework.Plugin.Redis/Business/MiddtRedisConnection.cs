using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace Middt.Framework.Plugin.Redis;

/// <summary>
/// Singleton owner of the shared <see cref="IConnectionMultiplexer"/> and <see cref="RedLockFactory"/>.
/// The connection is established lazily on first use.
/// </summary>
public sealed class MiddtRedisConnection : IDisposable, IAsyncDisposable
{
    private readonly Lazy<IConnectionMultiplexer> _multiplexer;
    private readonly Lazy<RedLockFactory> _redLockFactory;

    public MiddtRedisConnection(IOptions<RedisSettings> options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;

        _multiplexer = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(settings.ConnectionString),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _redLockFactory = new Lazy<RedLockFactory>(
            () => RedLockFactory.Create([new RedLockMultiplexer(_multiplexer.Value)], loggerFactory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IConnectionMultiplexer Multiplexer => _multiplexer.Value;

    public RedLockFactory RedLockFactory => _redLockFactory.Value;

    public IDatabase GetDatabase(int database = -1) => Multiplexer.GetDatabase(database);

    public void Dispose()
    {
        if (_redLockFactory.IsValueCreated)
        {
            _redLockFactory.Value.Dispose();
        }

        if (_multiplexer.IsValueCreated)
        {
            _multiplexer.Value.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_redLockFactory.IsValueCreated)
        {
            _redLockFactory.Value.Dispose();
        }

        if (_multiplexer.IsValueCreated)
        {
            await _multiplexer.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
