using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Middt.Framework.Client.Security;

/// <summary>
/// Reads the claims of a JWT <b>without validating it</b>. Only use for UI decisions on the client;
/// the API remains the authority and validates every token it receives.
/// </summary>
public static class JwtClaimsReader
{
    public static ClaimsIdentity ReadIdentity(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ClaimsIdentity();
        }

        try
        {
            var jwt = new JsonWebToken(token);
            if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < DateTime.UtcNow)
            {
                return new ClaimsIdentity();
            }

            return new ClaimsIdentity(jwt.Claims, authenticationType: "jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        }
        catch (ArgumentException)
        {
            return new ClaimsIdentity();
        }
    }
}
