namespace Middt.Framework.Bff.Oidc;

/// <summary>
/// OpenID Connect sign-in of the BFF (configuration section <c>Bff:Oidc</c>). When <see cref="Authority"/> is set,
/// <c>/Account/Login</c> redirects to the identity provider (authorization code + PKCE) instead of showing the password
/// form; tokens are kept in the server side token store exactly like in password mode and never reach the browser.
/// </summary>
public sealed class MiddtBffOidcOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionName = "Bff:Oidc";

    /// <summary>Issuer URL of the identity provider as browsers see it (e.g. <c>http://localhost:8083/</c>). Empty = OIDC mode off.</summary>
    public string? Authority { get; set; }

    /// <summary>Client id registered at the identity provider.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret (confidential client; keep it out of source control). Sent as <c>client_secret_post</c>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Space separated scopes requested at sign-in.</summary>
    public string Scopes { get; set; } = "openid profile email roles offline_access api";

    /// <summary>
    /// Internal URL of the identity provider for server-to-server calls (discovery, token, userinfo, revocation), e.g.
    /// <c>http://middt-identity:8080/</c> in a container network. Browser redirects keep using <see cref="Authority"/>.
    /// </summary>
    public string? BackchannelAuthority { get; set; }

    /// <summary>Requires HTTPS for the metadata address; <c>null</c> = required outside Development.</summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>Redirect URI path of the authorization response (register <c>{origin}{CallbackPath}</c> at the provider).</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Post logout redirect URI path (register <c>{origin}{SignedOutCallbackPath}</c> at the provider).</summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>Local page shown after the provider's logout returned to <see cref="SignedOutCallbackPath"/>.</summary>
    public string SignedOutRedirectUri { get; set; } = "/";

    /// <summary>
    /// Uses pushed authorization requests (PAR) when the provider advertises them. Off by default: the client needs
    /// the PAR endpoint permission at the provider.
    /// </summary>
    public bool UsePushedAuthorization { get; set; }

    /// <summary><c>true</c> when <see cref="Authority"/> is configured.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Authority);
}
