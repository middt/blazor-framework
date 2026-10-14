using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Middt.Identity;

/// <summary>Users, roles (ASP.NET Core Identity) and OpenIddict applications, scopes, authorizations and tokens (schema <c>idp</c>).</summary>
public sealed class MiddtIdentityDbContext(DbContextOptions<MiddtIdentityDbContext> options, IOptions<MiddtIdentityOptions> identityOptions)
    : IdentityDbContext<MiddtIdentityUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // SQLite has no schemas.
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) != true && identityOptions.Value.Schema is { Length: > 0 } schema)
        {
            builder.HasDefaultSchema(schema);
        }

        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.Entity<MiddtIdentityUser>(e => e.Property(u => u.DisplayName).HasMaxLength(256));
    }
}
