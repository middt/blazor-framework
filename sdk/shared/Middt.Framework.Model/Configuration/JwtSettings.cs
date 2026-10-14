using System.ComponentModel.DataAnnotations;

namespace Middt.Framework.Api.Configuration.Model;

public class JwtSettings
{
    public const string SectionName = nameof(JwtSettings);

    /// <summary>HMAC-SHA256 signing key. Must be at least 32 characters; keep it out of source control.</summary>
    [Required, MinLength(32)]
    public string Secret { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Access token lifetime in seconds.</summary>
    [Range(1, int.MaxValue)]
    public int Expires { get; set; } = 600;

    /// <summary>
    /// Absolute lifetime of a refresh token family in seconds (default 7 days). Rotated refresh tokens inherit
    /// the expiry of the family, so a session can never be extended beyond this without a new login.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RefreshTokenExpires { get; set; } = 7 * 24 * 60 * 60;
}
