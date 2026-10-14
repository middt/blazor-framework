using Microsoft.Extensions.Caching.Hybrid;

namespace Middt.Framework.Api.Cache;

/// <summary>
/// <see cref="IBaseCache"/> backed by <see cref="HybridCache"/>. When an <c>IDistributedCache</c>
/// (e.g. Redis) is registered it is used automatically as the second level.
/// </summary>
public sealed class BaseHybridCache(HybridCache cache) : IBaseCache
{
    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var options = expiration is null ? null : new HybridCacheEntryOptions { Expiration = expiration, LocalCacheExpiration = expiration };
        return cache.GetOrCreateAsync(key, factory, options, cancellationToken: cancellationToken);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);
}
