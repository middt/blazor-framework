using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity.Ui.Components.Admin;

/// <summary>
/// Editable settings of an OpenIddict application (client) and their mapping to/from
/// <see cref="OpenIddictApplicationDescriptor"/>. Permissions the UI does not manage are kept unchanged.
/// </summary>
public sealed class ClientSettings
{
    /// <summary>Endpoint permissions offered by the UI (value → Turkish label).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> EndpointOptions { get; } =
    [
        new(Permissions.Endpoints.Authorization, "Yetkilendirme (/connect/authorize)"),
        new(Permissions.Endpoints.Token, "Token (/connect/token)"),
        new(Permissions.Endpoints.EndSession, "Oturum kapatma (/connect/logout)"),
        new(Permissions.Endpoints.Revocation, "İptal (/connect/revoke)"),
        new(Permissions.Endpoints.Introspection, "İnceleme (/connect/introspect)"),
    ];

    /// <summary>Grant type permissions offered by the UI.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> GrantTypeOptions { get; } =
    [
        new(Permissions.GrantTypes.AuthorizationCode, "Authorization code"),
        new(Permissions.GrantTypes.RefreshToken, "Refresh token"),
        new(Permissions.GrantTypes.ClientCredentials, "Client credentials"),
    ];

    /// <summary>Standard scopes that need a permission (<c>openid</c>/<c>offline_access</c> do not).</summary>
    public static IReadOnlyList<string> StandardScopes { get; } = [OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.Roles, OpenIddictConstants.Scopes.Phone, OpenIddictConstants.Scopes.Address];

    private static readonly string[] ManagedPrefixes =
        [Permissions.Prefixes.Endpoint, Permissions.Prefixes.GrantType, Permissions.Prefixes.Scope, Permissions.Prefixes.ResponseType];

    public string? ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string ClientType { get; set; } = ClientTypes.Confidential;

    public string ConsentType { get; set; } = ConsentTypes.Explicit;

    /// <summary>One absolute URI per line.</summary>
    public string? RedirectUris { get; set; }

    /// <summary>One absolute URI per line.</summary>
    public string? PostLogoutRedirectUris { get; set; }

    public IEnumerable<string> Endpoints { get; set; } = [Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession, Permissions.Endpoints.Revocation];

    public IEnumerable<string> GrantTypes { get; set; } = [Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken];

    /// <summary>Scope names (without the <c>scp:</c> prefix).</summary>
    public IEnumerable<string> Scopes { get; set; } = [OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.Roles];

    public bool RequirePkce { get; set; } = true;

    /// <summary>Settings of an existing application.</summary>
    public static ClientSettings From(OpenIddictApplicationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ClientSettings
        {
            ClientId = descriptor.ClientId,
            DisplayName = descriptor.DisplayName,
            ClientType = descriptor.ClientType ?? ClientTypes.Public,
            ConsentType = descriptor.ConsentType ?? ConsentTypes.Explicit,
            RedirectUris = string.Join('\n', descriptor.RedirectUris.Select(u => u.OriginalString)),
            PostLogoutRedirectUris = string.Join('\n', descriptor.PostLogoutRedirectUris.Select(u => u.OriginalString)),
            Endpoints = [.. descriptor.Permissions.Where(p => p.StartsWith(Permissions.Prefixes.Endpoint, StringComparison.Ordinal))],
            GrantTypes = [.. descriptor.Permissions.Where(p => p.StartsWith(Permissions.Prefixes.GrantType, StringComparison.Ordinal))],
            Scopes = [.. descriptor.Permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal)).Select(p => p[Permissions.Prefixes.Scope.Length..])],
            RequirePkce = descriptor.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange),
        };
    }

    /// <summary>
    /// Writes the settings into <paramref name="descriptor"/> (populated from the stored application on edit). Returns
    /// validation messages; the descriptor is only valid when the list is empty.
    /// </summary>
    public IReadOnlyList<string> ApplyTo(OpenIddictApplicationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errors.Add("İstemci kimliği (client id) zorunludur.");
        }

        descriptor.ClientId = ClientId?.Trim();
        descriptor.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
        descriptor.ClientType = ClientType;
        descriptor.ConsentType = ConsentType;
        if (ClientType == ClientTypes.Public)
        {
            descriptor.ClientSecret = null;
        }

        ReplaceUris(descriptor.RedirectUris, RedirectUris, "Yönlendirme adresi", errors);
        ReplaceUris(descriptor.PostLogoutRedirectUris, PostLogoutRedirectUris, "Çıkış sonrası adres", errors);

        descriptor.Permissions.RemoveWhere(p => ManagedPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal)));
        descriptor.Permissions.UnionWith(Endpoints);
        descriptor.Permissions.UnionWith(GrantTypes);
        descriptor.Permissions.UnionWith(Scopes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Permissions.Prefixes.Scope + s.Trim()));
        if (GrantTypes.Contains(Permissions.GrantTypes.AuthorizationCode))
        {
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            if (descriptor.RedirectUris.Count == 0)
            {
                errors.Add("Authorization code için en az bir yönlendirme adresi gerekir.");
            }
        }

        if (ClientType == ClientTypes.Public && GrantTypes.Contains(Permissions.GrantTypes.ClientCredentials))
        {
            errors.Add("Public istemciler client credentials kullanamaz.");
        }

        if (RequirePkce)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }
        else
        {
            descriptor.Requirements.Remove(Requirements.Features.ProofKeyForCodeExchange);
        }

        return errors;
    }

    /// <summary>A new random client secret (256 bit, base64url); shown once, stored hashed by OpenIddict.</summary>
    public static string GenerateSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static void ReplaceUris(HashSet<Uri> target, string? text, string label, List<string> errors)
    {
        target.Clear();
        foreach (var line in (text ?? string.Empty).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Absolute, no fragment (RFC 6749 3.1.2); custom schemes are allowed for native apps.
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && !uri.IsFile && string.IsNullOrEmpty(uri.Fragment))
            {
                target.Add(uri);
            }
            else
            {
                errors.Add($"{label} geçersiz: {line}");
            }
        }
    }
}
