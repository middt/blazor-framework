using Microsoft.Extensions.Options;
using Middt.Framework.Common.Helper;
using StackExchange.Redis;

namespace Middt.Framework.Plugin.Redis;

/// <summary>JSON cache on Redis with distributed-lock protected population.</summary>
public class MiddtRedisCache(MiddtRedisConnection connection, MiddtRedislock redisLock, IOptions<RedisSettings> options)
{
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(200);

    private readonly int _database = options.Value.Database;

    public async Task<T?> ReadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var (_, value) = await TryReadAsync<T>(key, cancellationToken).ConfigureAwait(false);
        return value;
    }

    /// <summary>
    /// Returns the cached value, or produces it with <paramref name="factory"/> under a distributed lock
    /// (double-checked) and caches it for <paramref name="cacheTimeout"/>.
    /// Returns <c>default</c> only when the value is missing and the lock could not be acquired.
    /// </summary>
    public async Task<T?> ReadWriteAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan lockTimeout,
        TimeSpan cacheTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var (found, cached) = await TryReadAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (found)
        {
            return cached;
        }

        return await redisLock.ExecuteWithLockAsync(
            async ct =>
            {
                var (exists, current) = await TryReadAsync<T>(key, ct).ConfigureAwait(false);
                if (exists)
                {
                    return current;
                }

                var created = await factory(ct).ConfigureAwait(false);
                await SetAsync(key, created, cacheTimeout).ConfigureAwait(false);
                return created;
            },
            LockName(key),
            lockTimeout,
            LockWait,
            LockRetry,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Produces a value with <paramref name="factory"/> under a distributed lock and caches it.</summary>
    /// <returns><c>true</c> when the value was written; <c>false</c> when the lock could not be acquired.</returns>
    public Task<bool> WriteWithLockAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan lockTimeout,
        TimeSpan cacheTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return redisLock.ExecuteWithLockAsync(
            async ct =>
            {
                var value = await factory(ct).ConfigureAwait(false);
                await SetAsync(key, value, cacheTimeout).ConfigureAwait(false);
            },
            LockName(key),
            lockTimeout,
            LockWait,
            LockRetry,
            cancellationToken);
    }

    public Task<bool> DeleteKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetDatabase().KeyDeleteAsync(key);
    }

    private async Task<(bool Found, T? Value)> TryReadAsync<T>(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await GetDatabase().StringGetAsync(key).ConfigureAwait(false);
        return value.IsNullOrEmpty ? (false, default) : (true, JsonHelper.Deserialize<T>(value.ToString()));
    }

    private Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry) =>
        GetDatabase().StringSetAsync(key, JsonHelper.Serialize(value), expiry);

    private static string LockName(string key) => key + "_Lock";

    private IDatabase GetDatabase() => connection.GetDatabase(_database);
}
