using System.Security.Claims;

namespace Middt.Framework.Api.Security;

/// <summary>
/// Maps the OpenID Connect claims of access tokens issued by an authority (<c>sub</c>, <c>name</c>, <c>role</c>,
/// <c>email</c>) to the <see cref="ClaimTypes"/> the framework reads (<c>WorkflowUser</c>, <c>ExpressionUser</c>, hubs,
/// controllers), so both token modes look the same to application code.
/// </summary>
public static class AuthorityClaims
{
    /// <summary>Adds the <see cref="ClaimTypes"/> twins of the OIDC claims of <paramref name="identity"/> (idempotent).</summary>
    public static void Map(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var subject = identity.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(subject))
        {
            AddOnce(identity, ClaimTypes.NameIdentifier, subject);
            AddOnce(identity, ClaimTypes.Sid, subject);
        }

        var name = identity.FindFirst("name")?.Value ?? identity.FindFirst("preferred_username")?.Value ?? identity.FindFirst("email")?.Value;
        if (!string.IsNullOrEmpty(name))
        {
            AddOnce(identity, ClaimTypes.Name, name);
        }

        if (identity.FindFirst("email")?.Value is { Length: > 0 } email)
        {
            AddOnce(identity, ClaimTypes.Email, email);
        }

        foreach (var role in identity.FindAll("role").Select(c => c.Value).ToList())
        {
            AddOnce(identity, ClaimTypes.Role, role);
        }
    }

    private static void AddOnce(ClaimsIdentity identity, string type, string value)
    {
        if (!identity.HasClaim(type, value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }
}
