namespace Middt.Framework.Api.Security;

/// <summary>A stored refresh token. Tokens of one login share a <see cref="FamilyId"/> and its absolute expiry.</summary>
public sealed record RefreshTokenEntry(long UserId, string FamilyId, DateTimeOffset ExpiresAt, DateTimeOffset? ConsumedAt = null);

public enum RefreshTokenStatus
{
    Valid,
    NotFound,
    Reused,
    Revoked,
}

public sealed record RefreshTokenValidation(RefreshTokenStatus Status, RefreshTokenEntry? Entry);

/// <summary>
/// Persistence for refresh tokens with rotation and reuse detection (OAuth 2.0 Security BCP):
/// every refresh token is single use; presenting an already consumed token revokes its whole family.
/// Only a hash of the token is stored.
/// </summary>
public interface IRefreshTokenStore
{
    Task StoreAsync(string refreshToken, RefreshTokenEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Validates and consumes a token in one step. A second consumption reports <see cref="RefreshTokenStatus.Reused"/>.</summary>
    Task<RefreshTokenValidation> ConsumeAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<RefreshTokenEntry?> FindAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task RevokeFamilyAsync(string familyId, DateTimeOffset familyExpiresAt, CancellationToken cancellationToken = default);
}
