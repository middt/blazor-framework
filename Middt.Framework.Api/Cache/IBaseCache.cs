namespace Middt.Framework.Api.Cache;

/// <summary>Read-through cache abstraction (stampede protected, L1 memory + optional L2 distributed).</summary>
public interface IBaseCache
{
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
