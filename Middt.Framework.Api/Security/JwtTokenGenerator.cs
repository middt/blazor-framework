using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Api.Security;

/// <summary>HMAC-SHA256 JWT issuer based on <see cref="JsonWebTokenHandler"/>.</summary>
public sealed class JwtTokenGenerator(IOptions<JwtSettings> options, TimeProvider timeProvider) : IJwtTokenGenerator
{
    private readonly JsonWebTokenHandler handler = new() { MapInboundClaims = false };

    public TokenResponseModel CreateToken(IEnumerable<Claim> claims)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var accessToken = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(settings.Expires),
            SigningCredentials = new SigningCredentials(CreateSigningKey(settings), SecurityAlgorithms.HmacSha256),
        });

        return new TokenResponseModel
        {
            access_token = accessToken,
            expires_in = settings.Expires,
            refresh_token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
        };
    }

    public async Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token)
    {
        var parameters = CreateValidationParameters(options.Value);
        parameters.ValidateLifetime = false;

        var result = await handler.ValidateTokenAsync(token, parameters);
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    /// <summary>Token validation rules shared by the JwtBearer handler and the refresh flow.</summary>
    public static TokenValidationParameters CreateValidationParameters(JwtSettings settings) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(settings),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
    };

    private static SymmetricSecurityKey CreateSigningKey(JwtSettings settings) =>
        new(Encoding.UTF8.GetBytes(settings.Secret));
}
