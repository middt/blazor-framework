using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Blazor.Web.Security;

/// <summary>
/// <see cref="IServerTokenStore"/> on <see cref="IDistributedCache"/> (in-memory by default; register Redis for
/// multiple instances). Entries are encrypted with Data Protection, so cache dumps do not expose tokens.
/// Idle and absolute timeouts are enforced here, not by the cache's sliding expiration, so that background
/// revalidation can check a session without keeping it alive.
/// </summary>
public sealed class DistributedServerTokenStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtection,
    IOptions<GeneralSettings> settings,
    TimeProvider timeProvider) : IServerTokenStore
{
    private const string Prefix = "middt:session:";
    private static readonly TimeSpan TouchThrottle = TimeSpan.FromSeconds(30);

    private readonly IDataProtector protector = dataProtection.CreateProtector("Middt.ServerTokenStore.v1");

    public async Task<SaveTokenResponseModel?> GetAsync(string sessionId, bool touch, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(Prefix + sessionId, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        Entry entry;
        try
        {
            entry = JsonSerializer.Deserialize<Entry>(protector.Unprotect(bytes))!;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Data Protection keys changed (e.g. not persisted) while the cache survived: treat as signed out.
            await RemoveAsync(sessionId, cancellationToken);
            return null;
        }
        var now = timeProvider.GetUtcNow();

        if (entry.Tokens.expire_datetime <= now || now - entry.LastActivity > IdleTimeout)
        {
            await RemoveAsync(sessionId, cancellationToken);
            return null;
        }

        if (touch && now - entry.LastActivity > TouchThrottle)
        {
            await WriteAsync(sessionId, entry with { LastActivity = now }, cancellationToken);
        }

        return entry.Tokens;
    }

    public Task SetAsync(string sessionId, SaveTokenResponseModel tokens, CancellationToken cancellationToken = default)
    {
        tokens.expire_datetime ??= timeProvider.GetUtcNow().AddMinutes(settings.Value.SessionAbsoluteTimeoutInMinute);
        return WriteAsync(sessionId, new Entry(tokens, timeProvider.GetUtcNow()), cancellationToken);
    }

    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(Prefix + sessionId, cancellationToken);

    private TimeSpan IdleTimeout => TimeSpan.FromMinutes(settings.Value.SessionTimeoutInMinute);

    private Task WriteAsync(string sessionId, Entry entry, CancellationToken cancellationToken) =>
        cache.SetAsync(
            Prefix + sessionId,
            protector.Protect(JsonSerializer.SerializeToUtf8Bytes(entry)),
            new DistributedCacheEntryOptions { AbsoluteExpiration = entry.Tokens.expire_datetime },
            cancellationToken);

    private sealed record Entry(SaveTokenResponseModel Tokens, DateTimeOffset LastActivity);
}
