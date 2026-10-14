using System.Security.Claims;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Api.Security;

public interface IJwtTokenGenerator
{
    /// <summary>Creates a signed access token plus a cryptographically random refresh token.</summary>
    TokenResponseModel CreateToken(IEnumerable<Claim> claims);

    /// <summary>
    /// Validates the signature, issuer and audience of a (possibly expired) access token and returns its principal,
    /// or <c>null</c> when the token is not one of ours. Used by the refresh flow.
    /// </summary>
    Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token);
}
