using Microsoft.Extensions.Logging;
using RedLockNet;

namespace Middt.Framework.Plugin.Redis;

/// <summary>Distributed locking helpers on top of RedLock.net.</summary>
public partial class MiddtRedisLock(MiddtRedisConnection connection, ILogger<MiddtRedisLock> logger)
{
    public Task<IRedLock> CreateLockAsync(string resource, TimeSpan expiry) =>
        connection.RedLockFactory.CreateLockAsync(resource, expiry);

    public Task<IRedLock> CreateLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken cancellationToken = default) =>
        connection.RedLockFactory.CreateLockAsync(resource, expiry, wait, retry, cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> while holding the distributed lock on <paramref name="resource"/>.
    /// Returns <c>default</c> when the lock could not be acquired. Exceptions thrown by the action propagate.
    /// </summary>
    public virtual async Task<T?> ExecuteWithLockAsync<T>(
        Func<CancellationToken, Task<T>> action,
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var redLock = await CreateLockAsync(resource, expiry, wait, retry, cancellationToken).ConfigureAwait(false);
        if (!redLock.IsAcquired)
        {
            LogLockNotAcquired(resource, redLock.Status);
            return default;
        }

        return await action(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="action"/> while holding the distributed lock on <paramref name="resource"/>.
    /// Returns <c>false</c> when the lock could not be acquired.
    /// </summary>
    public virtual async Task<bool> ExecuteWithLockAsync(
        Func<CancellationToken, Task> action,
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var redLock = await CreateLockAsync(resource, expiry, wait, retry, cancellationToken).ConfigureAwait(false);
        if (!redLock.IsAcquired)
        {
            LogLockNotAcquired(resource, redLock.Status);
            return false;
        }

        await action(cancellationToken).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not acquire distributed lock {Resource} (status: {Status})")]
    private partial void LogLockNotAcquired(string resource, RedLockStatus status);
}
