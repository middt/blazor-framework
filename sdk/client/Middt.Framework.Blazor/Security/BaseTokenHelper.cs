using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Middt.Framework.Blazor.Security;

/// <summary>Convenience accessor for the signed-in user's claims (backed by the app's <see cref="AuthenticationStateProvider"/>).</summary>
public class BaseTokenHelper(AuthenticationStateProvider authenticationStateProvider)
{
    public ClaimsIdentity ClaimsIdentity { get; private set; } = new();

    public bool IsAuthenticated => ClaimsIdentity.IsAuthenticated;

    public async Task<ClaimsIdentity> LoadAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsIdentity = state.User.Identity as ClaimsIdentity ?? new ClaimsIdentity();
        return ClaimsIdentity;
    }

    public long UserID() =>
        ClaimsIdentity.FindFirst(ClaimTypes.Sid) is { } claim && long.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : -1;

    public string Username() => ClaimsIdentity.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    public List<string> RoleList() => ClaimsIdentity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    public bool IsHasRoles(IEnumerable<string> roles) => roles.Any(IsHasRole);

    public bool IsHasRole(string role) => ClaimsIdentity.HasClaim(ClaimTypes.Role, role);
}
