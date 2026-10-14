using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Middt.Framework.Tests.Infrastructure;

/// <summary>
/// Dictionary backed <see cref="IDistributedCache"/> whose absolute expirations follow a (fake) <see cref="TimeProvider"/>,
/// so cache expiry and the code under test agree on "now". Exposes keys and raw values for assertions.
/// </summary>
public sealed class TestDistributedCache(TimeProvider timeProvider) : IDistributedCache
{
    private readonly ConcurrentDictionary<string, (byte[] Value, DateTimeOffset? ExpiresAt)> entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => [.. entries.Keys];

    public IReadOnlyCollection<byte[]> RawValues => [.. entries.Values.Select(e => e.Value)];

    public byte[]? Get(string key)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow())
        {
            entries.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var now = timeProvider.GetUtcNow();
        DateTimeOffset? expiresAt = options.AbsoluteExpiration
            ?? (options.AbsoluteExpirationRelativeToNow is { } relative ? now + relative : null);
        entries[key] = (value, expiresAt);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => entries.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}
