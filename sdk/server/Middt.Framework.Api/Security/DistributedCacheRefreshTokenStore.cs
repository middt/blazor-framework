using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Middt.Framework.Common.Observability;

namespace Middt.Framework.Api.Security;

/// <summary>
/// <see cref="IRefreshTokenStore"/> on <see cref="IDistributedCache"/> (in-memory by default, Redis when registered).
/// Keys are SHA-256 hashes, so a cache dump does not reveal usable tokens.
/// </summary>
/// <remarks>
/// Consumption is check-then-set, not atomic: two simultaneous refreshes with the same token may both succeed.
/// Clients should serialise refreshes per session (the Middt API clients do). Use a database backed store with a
/// conditional update when strict single use across concurrent callers is required.
/// </remarks>
public sealed class DistributedCacheRefreshTokenStore(IDistributedCache cache, TimeProvider timeProvider) : IRefreshTokenStore
{
    private const string TokenPrefix = "middt:rt:";
    private const string FamilyPrefix = "middt:rtf:";

    public async Task StoreAsync(string refreshToken, RefreshTokenEntry entry, CancellationToken cancellationToken = default)
    {
        using var activity = MiddtDiagnostics.ApiActivitySource.StartActivity("auth.refresh_token.store");
        activity?.SetTag("middt.auth.family_id", entry.FamilyId);
        await cache.SetAsync(TokenPrefix + Hash(refreshToken), JsonSerializer.SerializeToUtf8Bytes(entry), Until(entry.ExpiresAt), cancellationToken);
    }

    public async Task<RefreshTokenEntry?> FindAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var activity = MiddtDiagnostics.ApiActivitySource.StartActivity("auth.refresh_token.find");
        var bytes = await cache.GetAsync(TokenPrefix + Hash(refreshToken), cancellationToken);
        activity?.SetTag("middt.auth.found", bytes is not null);
        return bytes is null ? null : JsonSerializer.Deserialize<RefreshTokenEntry>(bytes);
    }

    public async Task<RefreshTokenValidation> ConsumeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var activity = MiddtDiagnostics.ApiActivitySource.StartActivity("auth.refresh_token.consume");
        var entry = await FindAsync(refreshToken, cancellationToken);
        if (entry is null || entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            activity?.SetTag("middt.auth.status", nameof(RefreshTokenStatus.NotFound));
            return new(RefreshTokenStatus.NotFound, entry);
        }

        if (await cache.GetAsync(FamilyPrefix + entry.FamilyId, cancellationToken) is not null)
        {
            activity?.SetTag("middt.auth.status", nameof(RefreshTokenStatus.Revoked));
            return new(RefreshTokenStatus.Revoked, entry);
        }

        if (entry.ConsumedAt is not null)
        {
            // Replay of a rotated token: either an attacker or a buggy client holds a copy. Kill the session.
            await RevokeFamilyAsync(entry.FamilyId, entry.ExpiresAt, cancellationToken);
            activity?.SetTag("middt.auth.status", nameof(RefreshTokenStatus.Reused));
            return new(RefreshTokenStatus.Reused, entry);
        }

        var consumed = entry with { ConsumedAt = timeProvider.GetUtcNow() };
        await StoreAsync(refreshToken, consumed, cancellationToken);
        activity?.SetTag("middt.auth.status", nameof(RefreshTokenStatus.Valid));
        return new(RefreshTokenStatus.Valid, consumed);
    }

    public async Task RevokeFamilyAsync(string familyId, DateTimeOffset familyExpiresAt, CancellationToken cancellationToken = default)
    {
        using var activity = MiddtDiagnostics.ApiActivitySource.StartActivity("auth.refresh_token.revoke_family");
        activity?.SetTag("middt.auth.family_id", familyId);
        await cache.SetAsync(FamilyPrefix + familyId, [1], Until(familyExpiresAt), cancellationToken);
    }

    private DistributedCacheEntryOptions Until(DateTimeOffset expiresAt) =>
        new() { AbsoluteExpiration = expiresAt > timeProvider.GetUtcNow() ? expiresAt : timeProvider.GetUtcNow().AddMinutes(1) };

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
