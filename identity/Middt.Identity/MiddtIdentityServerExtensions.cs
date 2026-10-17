using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Middt.Identity.Journeys;
using Middt.Identity.Seeding;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Tasks;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity;

/// <summary>Hosting of the identity provider: <see cref="AddMiddtIdentityServer"/> + <c>MapMiddtIdentity</c>.</summary>
public static class MiddtIdentityServerExtensions
{
    /// <summary>
    /// Registers the identity provider: ASP.NET Core Identity (users/roles stored in the workflow database), OpenIddict (core
    /// stores in <see cref="Middt.Framework.Workflow.Persistence.WorkflowDbContext"/>, server with authorization code + PKCE,
    /// refresh token rotation, client credentials, end session, revocation, introspection, userinfo), the IdP session cookie
    /// (<see cref="MiddtIdentityDefaults.CookieScheme"/>), the journey engine (Middt workflow engine with the default
    /// journeys) and <see cref="IAuthJourneyService"/>, a development mailbox and the startup seeding.
    /// <code>
    /// builder.AddMiddtIdentityServer(o => o
    ///     .UseDbContext(db => db.UseSqlServer(connectionString))
    ///     .AddSeedFromConfiguration(builder.Configuration.GetSection("Identity:Seed")));
    /// …
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.MapMiddtIdentity();
    /// </code>
    /// The signing certificate is bound from <c>Identity:SigningCertificate</c> (<c>Path</c>, <c>Password</c> or <c>Thumbprint</c>)
    /// before <paramref name="configure"/> runs; Development falls back to development certificates.
    /// </summary>
    public static WebApplicationBuilder AddMiddtIdentityServer(this WebApplicationBuilder builder, Action<MiddtIdentityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MiddtIdentityOptions();
        builder.Configuration.GetSection("Identity:SigningCertificate").Bind(options.SigningCertificate);
        configure(options);
        if (options.ConfigureDbContext is null)
        {
            throw new InvalidOperationException("Configure the identity database with options.UseDbContext(...).");
        }

        var environment = builder.Environment;
        var services = builder.Services;

        services.AddSingleton(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();

        services.AddIdentityCore<MiddtIdentityUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                identity.SignIn.RequireConfirmedEmail = options.RequireConfirmedEmail;
                foreach (var configureIdentity in options.IdentityConfigurations)
                {
                    configureIdentity(identity);
                }
            })
            .AddRoles<IdentityRole>()
            .AddUserStore<WorkflowUserStore>()
            .AddRoleStore<WorkflowRoleStore>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(MiddtIdentityDefaults.CookieScheme)
            .AddCookie(MiddtIdentityDefaults.CookieScheme, cookie =>
            {
                cookie.Cookie.Name = MiddtIdentityDefaults.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.Cookie.IsEssential = true;
                cookie.LoginPath = MiddtIdentityDefaults.LoginPath;
                cookie.LogoutPath = MiddtIdentityDefaults.LogoutPath;
                cookie.AccessDeniedPath = MiddtIdentityDefaults.AccessDeniedPath;
                cookie.ReturnUrlParameter = "returnUrl";
                cookie.ExpireTimeSpan = options.SessionLifetime;
                cookie.SlidingExpiration = true;
            });
        services.AddAuthorization();

        AddOpenIddict(services, options, environment);
        AddJourneys(services, options, environment);

        services.TryAddSingleton<DevelopmentMailbox>();
        services.TryAddSingleton<IIdentityMessageSender>(sp => sp.GetRequiredService<DevelopmentMailbox>());
        services.AddScoped<IAuthJourneyService, AuthJourneyService>();
        services.AddHostedService<IdentitySeeder>();
        return builder;
    }

    private static void AddOpenIddict(IServiceCollection services, MiddtIdentityOptions options, IHostEnvironment environment)
    {
        services.AddOpenIddict()
            .AddCore(core =>
            {
                core.SetDefaultApplicationEntity<WorkflowOpenIddictApplication>()
                    .SetDefaultAuthorizationEntity<WorkflowOpenIddictAuthorization>()
                    .SetDefaultScopeEntity<WorkflowOpenIddictScope>()
                    .SetDefaultTokenEntity<WorkflowOpenIddictToken>();

                core.ReplaceApplicationStore<WorkflowOpenIddictApplication, WorkflowOpenIddictApplicationStore>()
                    .ReplaceAuthorizationStore<WorkflowOpenIddictAuthorization, WorkflowOpenIddictAuthorizationStore>()
                    .ReplaceScopeStore<WorkflowOpenIddictScope, WorkflowOpenIddictScopeStore>()
                    .ReplaceTokenStore<WorkflowOpenIddictToken, WorkflowOpenIddictTokenStore>();
            })
            .AddServer(server =>
            {
                if (!string.IsNullOrWhiteSpace(options.Issuer))
                {
                    server.SetIssuer(new Uri(options.Issuer, UriKind.Absolute));
                    server.AddEventHandler(Endpoints.IssuerEndpointRebaser.Descriptor);
                }

                server.SetAuthorizationEndpointUris(MiddtIdentityEndpoints.AuthorizePath)
                    .SetTokenEndpointUris(MiddtIdentityEndpoints.TokenPath)
                    .SetUserInfoEndpointUris(MiddtIdentityEndpoints.UserInfoPath)
                    .SetEndSessionEndpointUris(MiddtIdentityEndpoints.EndSessionPath)
                    .SetRevocationEndpointUris(MiddtIdentityEndpoints.RevocationPath)
                    .SetIntrospectionEndpointUris(MiddtIdentityEndpoints.IntrospectionPath);

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow()
                    .RequireProofKeyForCodeExchange();

                server.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess);
                server.RegisterClaims(Claims.Subject, Claims.Name, Claims.PreferredUsername, Claims.Email, Claims.EmailVerified, Claims.Role);

                server.SetAccessTokenLifetime(options.AccessTokenLifetime)
                    .SetIdentityTokenLifetime(options.IdentityTokenLifetime)
                    .SetRefreshTokenLifetime(options.RefreshTokenLifetime)
                    .SetAuthorizationCodeLifetime(options.AuthorizationCodeLifetime);

                // APIs validate access tokens with the discovery keys only (no shared decryption key).
                server.DisableAccessTokenEncryption();
                AddKeys(server, options, environment);

                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (!(options.RequireHttps ?? !environment.IsDevelopment()))
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });
    }

    private static void AddKeys(OpenIddictServerBuilder server, MiddtIdentityOptions options, IHostEnvironment environment)
    {
        var certificates = options.SigningCertificate;
        if (options.EphemeralKeys)
        {
            server.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        else if (!string.IsNullOrEmpty(certificates.Path) || !string.IsNullOrEmpty(certificates.Thumbprint))
        {
            var signing = LoadCertificate(certificates.Path, certificates.Password, certificates.Thumbprint, environment);
            var encryption = !string.IsNullOrEmpty(certificates.EncryptionPath) || !string.IsNullOrEmpty(certificates.EncryptionThumbprint)
                ? LoadCertificate(certificates.EncryptionPath, certificates.EncryptionPassword, certificates.EncryptionThumbprint, environment)
                : signing;
            server.AddSigningCertificate(signing).AddEncryptionCertificate(encryption);
        }
        else if (environment.IsDevelopment())
        {
            server.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else
        {
            throw new InvalidOperationException(
                "No signing certificate configured for the identity provider: set Identity:SigningCertificate:Path (+ Password) or :Thumbprint.");
        }
    }

    private static X509Certificate2 LoadCertificate(string? path, string? password, string? thumbprint, IHostEnvironment environment)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var file = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(environment.ContentRootPath, path);
            return X509CertificateLoader.LoadPkcs12FromFile(file, password, X509KeyStorageFlags.DefaultKeySet);
        }

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint!, validOnly: false);
            if (found.Count > 0)
            {
                return found[0];
            }
        }

        throw new InvalidOperationException($"Signing certificate '{thumbprint}' was not found in the CurrentUser/LocalMachine 'My' store.");
    }

    private static void AddJourneys(IServiceCollection services, MiddtIdentityOptions options, IHostEnvironment environment)
    {
        services.AddScoped<IWorkflowOperationDispatcher, IdentityJourneyOperationDispatcher>();
        var overrides = new List<(string Key, string Json, string Source)>();
        foreach (var directory in options.JourneyDirectories)
        {
            var root = System.IO.Path.IsPathRooted(directory) ? directory : System.IO.Path.Combine(environment.ContentRootPath, directory);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Journey directory '{root}' does not exist.");
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var json = File.ReadAllText(file);
                overrides.Add((KeyOf(json, file), json, file));
            }
        }

        var overridden = overrides.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var assembly = typeof(MiddtIdentityServerExtensions).Assembly;
        var defaults = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && n.Contains(".Journeys.", StringComparison.Ordinal))
            .Select(n => ReadResource(assembly, n))
            .Where(d => !overridden.Contains(d.Key))
            .ToList();

        services.AddMiddtWorkflow(workflow =>
        {
            workflow.UseDbContext(options.ConfigureDbContext!);
            workflow.Schema = options.WorkflowSchema;
            workflow.AutoCreateSchema = options.AutoCreateSchema;
            foreach (var (_, json, source) in defaults.Concat(overrides))
            {
                workflow.AddDefinition(json, source);
            }

            foreach (var configureWorkflow in options.WorkflowConfigurations)
            {
                configureWorkflow(workflow);
            }
        });
    }

    private static (string Key, string Json, string Source) ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return (KeyOf(json, name), json, name);
    }

    private static string KeyOf(string json, string source)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["key"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Journey '{source}' has no key.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Journey '{source}' is not valid JSON: {ex.Message}", ex);
        }
    }
}
