using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity.Seeding;

/// <summary>
/// Startup: creates the identity tables (<see cref="MiddtIdentityOptions.AutoCreateSchema"/>) and applies the seed
/// idempotently – roles and users are created when missing (existing users only get missing roles), scopes and clients
/// are created or updated (redirect URIs, permissions, consent type …; an existing client secret is never replaced).
/// </summary>
internal sealed partial class IdentitySeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<MiddtIdentityOptions> options,
    IHostEnvironment environment,
    TimeProvider time,
    ILogger<IdentitySeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (options.Value.AutoCreateSchema)
        {
            await EnsureTablesAsync(services.GetRequiredService<MiddtIdentityDbContext>(), options.Value.Schema, cancellationToken);
        }

        if (!environment.IsDevelopment() && services.GetRequiredService<IIdentityMessageSender>() is DevelopmentMailbox)
        {
            LogDevelopmentMailbox(logger);
        }

        var seed = options.Value.Seed;
        await SeedRolesAsync(services.GetRequiredService<RoleManager<IdentityRole>>(), seed);
        await SeedScopesAsync(services.GetRequiredService<IOpenIddictScopeManager>(), seed, cancellationToken);
        await SeedClientsAsync(services.GetRequiredService<IOpenIddictApplicationManager>(), seed, cancellationToken);
        await SeedUsersAsync(services.GetRequiredService<UserManager<MiddtIdentityUser>>(), seed);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedRolesAsync(RoleManager<IdentityRole> roles, MiddtIdentitySeed seed)
    {
        var names = seed.Roles.Concat(seed.Users.SelectMany(u => u.Roles)).Append(options.Value.AdminRole)
            .Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!await roles.RoleExistsAsync(name))
            {
                Check(await roles.CreateAsync(new IdentityRole(name)), $"role '{name}'");
            }
        }
    }

    private static async Task SeedScopesAsync(IOpenIddictScopeManager scopes, MiddtIdentitySeed seed, CancellationToken cancellationToken)
    {
        foreach (var item in seed.Scopes)
        {
            var descriptor = new OpenIddictScopeDescriptor { Name = item.Name, DisplayName = item.DisplayName, Description = item.Description };
            descriptor.Resources.UnionWith(item.Resources);

            if (await scopes.FindByNameAsync(item.Name, cancellationToken) is { } existing)
            {
                await scopes.UpdateAsync(existing, descriptor, cancellationToken);
            }
            else
            {
                await scopes.CreateAsync(descriptor, cancellationToken);
            }
        }
    }

    private static async Task SeedClientsAsync(IOpenIddictApplicationManager applications, MiddtIdentitySeed seed, CancellationToken cancellationToken)
    {
        foreach (var client in seed.Clients)
        {
            var existing = await applications.FindByClientIdAsync(client.ClientId, cancellationToken);
            var descriptor = new OpenIddictApplicationDescriptor();
            if (existing is not null)
            {
                // Start from the stored values: keeps the (hashed) secret, which may have been rotated in the admin UI.
                await applications.PopulateAsync(descriptor, existing, cancellationToken);
            }

            Apply(descriptor, client);
            if (existing is null)
            {
                await applications.CreateAsync(descriptor, cancellationToken);
            }
            else
            {
                await applications.UpdateAsync(existing, descriptor, cancellationToken);
            }
        }
    }

    /// <summary>Maps a seed client to an OpenIddict descriptor (permissions from grant types and scopes; PKCE for the code flow).</summary>
    internal static void Apply(OpenIddictApplicationDescriptor descriptor, MiddtIdentitySeedClient client)
    {
        var isPublic = string.Equals(client.Type, ClientTypes.Public, StringComparison.OrdinalIgnoreCase);
        descriptor.ClientId = client.ClientId;
        descriptor.DisplayName = client.DisplayName ?? client.ClientId;
        descriptor.ClientType = isPublic ? ClientTypes.Public : ClientTypes.Confidential;
        descriptor.ConsentType = client.ConsentType.ToLowerInvariant() switch
        {
            "explicit" => ConsentTypes.Explicit,
            "external" => ConsentTypes.External,
            "systematic" => ConsentTypes.Systematic,
            _ => ConsentTypes.Implicit,
        };

        if (isPublic)
        {
            descriptor.ClientSecret = null;
        }
        else if (string.IsNullOrEmpty(descriptor.ClientSecret))
        {
            descriptor.ClientSecret = string.IsNullOrEmpty(client.ClientSecret)
                ? throw new InvalidOperationException($"Seed client '{client.ClientId}' is confidential and needs a ClientSecret.")
                : client.ClientSecret;
        }

        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.UnionWith(client.RedirectUris.Select(u => new Uri(u, UriKind.Absolute)));
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.UnionWith(client.PostLogoutRedirectUris.Select(u => new Uri(u, UriKind.Absolute)));

        var grantTypes = client.GrantTypes.Count > 0 ? client.GrantTypes : [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];
        descriptor.Permissions.Clear();
        descriptor.Requirements.Clear();
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        if (!isPublic)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Introspection);
        }

        foreach (var grantType in grantTypes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + grantType);
            if (grantType == GrantTypes.AuthorizationCode)
            {
                descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
                descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            }
        }

        foreach (var scope in client.Scopes.Where(s => s is not (Scopes.OpenId or Scopes.OfflineAccess)))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }
    }

    private async Task SeedUsersAsync(UserManager<MiddtIdentityUser> users, MiddtIdentitySeed seed)
    {
        foreach (var item in seed.Users)
        {
            var user = await users.FindByNameAsync(item.UserName);
            if (user is null)
            {
                user = new MiddtIdentityUser
                {
                    UserName = item.UserName,
                    Email = item.Email,
                    EmailConfirmed = true,
                    DisplayName = item.DisplayName,
                    CreatedAt = time.GetUtcNow(),
                };

                // Seeded (demo) passwords bypass the password policy on purpose.
                if (!string.IsNullOrEmpty(item.Password))
                {
                    user.PasswordHash = users.PasswordHasher.HashPassword(user, item.Password);
                }

                Check(await users.CreateAsync(user), $"user '{item.UserName}'");
            }

            var missing = item.Roles.Except(await users.GetRolesAsync(user), StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                Check(await users.AddToRolesAsync(user, missing), $"roles of user '{item.UserName}'");
            }
        }
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding {what} failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    /// <summary>Creates the database (if needed) and the tables of this context when they do not exist yet.</summary>
    internal static async Task EnsureTablesAsync(MiddtIdentityDbContext db, string? schema, CancellationToken cancellationToken)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var sqlite = provider.Contains("Sqlite", StringComparison.Ordinal);
        var sqlServer = provider.Contains("SqlServer", StringComparison.Ordinal);
        bool exists;
        if (sqlite)
        {
            exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'")
                .SingleAsync(cancellationToken) > 0;
        }
        else
        {
            var tableSchema = schema ?? (sqlServer ? "dbo" : "public");
            exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {tableSchema} AND table_name = 'AspNetUsers'")
                .SingleAsync(cancellationToken) > 0;
        }

        if (exists)
        {
            return;
        }

        if (!sqlite && schema is { Length: > 0 })
        {
            if (!SchemaName().IsMatch(schema))
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Invalid identity schema name '{schema}'."));
            }

#pragma warning disable EF1002 // schema is a validated identifier
            if (sqlServer)
            {
                await db.Database.ExecuteSqlRawAsync($"IF SCHEMA_ID(N'{schema}') IS NULL EXEC('CREATE SCHEMA [{schema}]')", cancellationToken);
            }
            else if (provider.Contains("Npgsql", StringComparison.Ordinal))
            {
                await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
            }
#pragma warning restore EF1002
        }

        await creator.CreateTablesAsync(cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The identity provider uses the development mailbox outside Development: e-mail codes are not delivered. Register an IIdentityMessageSender.")]
    private static partial void LogDevelopmentMailbox(ILogger logger);
}
