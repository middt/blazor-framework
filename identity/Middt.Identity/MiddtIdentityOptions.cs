using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Middt.Framework.Workflow;

namespace Middt.Identity;

/// <summary>Configuration of <see cref="MiddtIdentityServerExtensions.AddMiddtIdentityServer"/>.</summary>
public sealed class MiddtIdentityOptions
{
    /// <summary>Public base URL of the identity provider (<c>iss</c>), e.g. <c>http://localhost:8083/</c>. Empty = the request's host.</summary>
    public string? Issuer { get; set; }

    /// <summary>Database schema of the identity and OpenIddict tables (ignored by SQLite).</summary>
    public string? Schema { get; set; } = "idp";

    /// <summary>Database schema of the journey (workflow) tables in the same database (ignored by SQLite).</summary>
    public string? WorkflowSchema { get; set; } = "wf_idp";

    /// <summary>Creates the database/tables at startup when they do not exist (use EF migrations for production).</summary>
    public bool AutoCreateSchema { get; set; }

    /// <summary>
    /// Requires HTTPS for the protocol endpoints (OpenIddict transport security). Default: on outside Development.
    /// Behind a TLS terminating proxy keep it on and forward <c>X-Forwarded-Proto</c>.
    /// </summary>
    public bool? RequireHttps { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Lifetime of the identity provider's own session cookie (sliding).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Journeys not finished within this time are expired (the default journeys also time out after 15 minutes).</summary>
    public TimeSpan JourneyLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Wrong passwords/codes per journey before it ends in <c>failed</c> (account lockout is counted separately).</summary>
    public int MaxJourneyAttempts { get; set; } = 5;

    /// <summary>Validity of e-mail verification codes.</summary>
    public TimeSpan EmailCodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Role required for the administration UI.</summary>
    public string AdminRole { get; set; } = MiddtIdentityDefaults.AdminRole;

    /// <summary>Allows self registration (<c>auth-register</c>).</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>New users must confirm their e-mail with a code before they can sign in.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>Issuer name shown in authenticator apps (<c>otpauth://totp/{name}:{user}</c>).</summary>
    public string AuthenticatorIssuerName { get; set; } = "Middt";

    /// <summary>Signing/encryption keys (bound from <c>Identity:SigningCertificate</c> by default).</summary>
    public MiddtIdentityCertificateOptions SigningCertificate { get; set; } = new();

    /// <summary>Seed data applied idempotently at startup.</summary>
    public MiddtIdentitySeed Seed { get; } = new();

    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    internal List<Action<IdentityOptions>> IdentityConfigurations { get; } = [];

    internal List<Action<MiddtWorkflowOptions>> WorkflowConfigurations { get; } = [];

    internal List<string> JourneyDirectories { get; } = [];

    internal bool EphemeralKeys { get; private set; }

    /// <summary>Database of users, clients, tokens and journeys (one database, separate schemas).</summary>
    public MiddtIdentityOptions UseDbContext(Action<DbContextOptionsBuilder> configure)
    {
        ConfigureDbContext = configure;
        return this;
    }

    /// <summary>ASP.NET Core Identity options (password policy, lockout, user name characters …).</summary>
    public MiddtIdentityOptions ConfigureIdentity(Action<IdentityOptions> configure)
    {
        IdentityConfigurations.Add(configure);
        return this;
    }

    /// <summary>Workflow engine options of the journey engine (e.g. <c>UseViews</c>, <c>ExposeErrorDetails</c>, job processor).</summary>
    public MiddtIdentityOptions ConfigureWorkflow(Action<MiddtWorkflowOptions> configure)
    {
        WorkflowConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Loads journey definitions (<c>*.json</c>) from a directory (relative to the content root). A definition with the key of
    /// a default journey (e.g. <c>auth-login</c>) replaces it; other keys add journeys.
    /// </summary>
    public MiddtIdentityOptions AddJourneysFromDirectory(string directory)
    {
        JourneyDirectories.Add(directory);
        return this;
    }

    /// <summary>In-memory keys regenerated at every start: tests only (tokens do not survive a restart).</summary>
    public MiddtIdentityOptions UseEphemeralKeys()
    {
        EphemeralKeys = true;
        return this;
    }

    /// <summary>
    /// Adds seed data from configuration, e.g. <c>builder.Configuration.GetSection("Identity:Seed")</c> with <c>Roles</c>,
    /// <c>Scopes</c>, <c>Clients</c> and <c>Users</c> arrays (same shape as <see cref="MiddtIdentitySeed"/>).
    /// </summary>
    public MiddtIdentityOptions AddSeedFromConfiguration(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var seed = new MiddtIdentitySeed();
        section.Bind(seed);
        Seed.Roles.AddRange(seed.Roles);
        Seed.Scopes.AddRange(seed.Scopes);
        Seed.Clients.AddRange(seed.Clients);
        Seed.Users.AddRange(seed.Users);
        return this;
    }
}

/// <summary>Certificate used to sign tokens and encrypt codes/refresh tokens outside Development.</summary>
public sealed class MiddtIdentityCertificateOptions
{
    /// <summary>PKCS#12 file (relative to the content root).</summary>
    public string? Path { get; set; }

    public string? Password { get; set; }

    /// <summary>Alternative to <see cref="Path"/>: certificate in the CurrentUser/LocalMachine "My" store.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Separate encryption certificate (defaults to the signing certificate).</summary>
    public string? EncryptionPath { get; set; }

    public string? EncryptionPassword { get; set; }

    public string? EncryptionThumbprint { get; set; }
}

/// <summary>Seed data: created when missing; seeded clients and scopes are updated on every start (secrets are kept).</summary>
public sealed class MiddtIdentitySeed
{
    public List<string> Roles { get; set; } = [];

    public List<MiddtIdentitySeedScope> Scopes { get; set; } = [];

    public List<MiddtIdentitySeedClient> Clients { get; set; } = [];

    public List<MiddtIdentitySeedUser> Users { get; set; } = [];
}

/// <summary>An API scope, e.g. <c>api</c> with resource (audience) <c>middt-api</c>.</summary>
public sealed class MiddtIdentitySeedScope
{
    public string Name { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>Audiences of access tokens that contain this scope.</summary>
    public List<string> Resources { get; set; } = [];
}

/// <summary>An OIDC client (OpenIddict application).</summary>
public sealed class MiddtIdentitySeedClient
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Only used when the client is created (or has no secret yet): rotated secrets are not overwritten.</summary>
    public string? ClientSecret { get; set; }

    public string? DisplayName { get; set; }

    /// <summary><c>confidential</c> (default) or <c>public</c>.</summary>
    public string Type { get; set; } = "confidential";

    /// <summary><c>explicit</c> (consent screen), <c>implicit</c> (default, first-party), <c>external</c> or <c>systematic</c>.</summary>
    public string ConsentType { get; set; } = "implicit";

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    /// <summary><c>authorization_code</c>, <c>refresh_token</c>, <c>client_credentials</c> (default: code + refresh).</summary>
    public List<string> GrantTypes { get; set; } = [];

    /// <summary>Allowed scopes besides <c>openid</c> (e.g. <c>profile email roles offline_access api</c>).</summary>
    public List<string> Scopes { get; set; } = [];
}

public sealed class MiddtIdentitySeedUser
{
    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>Initial password; seeded passwords bypass the password policy (demo users such as <c>test</c>).</summary>
    public string? Password { get; set; }

    public string? DisplayName { get; set; }

    public List<string> Roles { get; set; } = [];
}
