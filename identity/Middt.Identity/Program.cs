using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Middt.Identity;
using Middt.Identity.Ui;

// Middt Identity: OAuth 2.0 / OpenID Connect server. Everything deployment specific comes from configuration
// (appsettings, environment variables, or an extra JSON file named by Identity:ConfigFile):
//   ConnectionStrings:Identity        SQL Server database (users, clients, tokens, journeys)
//   ConnectionStrings:redis           optional: shared Data Protection keys
//   Identity:Issuer                   public URL, e.g. https://id.example.com/
//   Identity:SigningCertificate:*     required outside Development
//   Identity:Seed:*                   roles, scopes, clients, users applied at every start (idempotent)
//   Identity:Password:*               password policy (ASP.NET Core Identity PasswordOptions)
//   Identity:*                        other MiddtIdentityOptions (lifetimes, AllowRegistration, AutoCreateSchema …)
var builder = WebApplication.CreateBuilder(args);
AddConfigFile(builder);

builder.AddMiddtIdentityServer(o =>
{
    var section = builder.Configuration.GetSection("Identity");
    section.Bind(o);
    o.UseDbContext(db => db.UseSqlServer(
        builder.Configuration.GetConnectionString("Identity")
        ?? throw new InvalidOperationException("ConnectionStrings:Identity is not configured.")));
    // Bind may already have filled the get-only Seed: start empty and read it exactly once.
    o.Seed.Roles.Clear();
    o.Seed.Scopes.Clear();
    o.Seed.Clients.Clear();
    o.Seed.Users.Clear();
    o.AddSeedFromConfiguration(section.GetSection("Seed"));
    o.ConfigureIdentity(identity => section.GetSection("Password").Bind(identity.Password));
    if (section["JourneyDirectory"] is { Length: > 0 } journeys)
    {
        o.AddJourneysFromDirectory(journeys);
    }
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMiddtIdentityUi();
builder.Services.AddHealthChecks();

// With Redis Data Protection keys (cookies, antiforgery) are shared between instances and survive restarts.
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redis;
        o.InstanceName = "middt-identity:";
    });
    builder.Services.AddDataProtection()
        .SetApplicationName("middt-identity")
        .PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis), "middt-identity:dataprotection-keys");
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(MiddtIdentityDefaults.AccessDeniedPath);
    app.UseHsts();
}

app.UseMiddtIdentitySecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapMiddtIdentity();
app.MapMiddtIdentityUi();

app.Run();

// Identity:ConfigFile adds a JSON file (relative to the content root or absolute), e.g. the demo's seed. Environment
// variables and the command line are added again afterwards so they still override it (secrets from the environment).
static void AddConfigFile(WebApplicationBuilder builder)
{
    if (builder.Configuration["Identity:ConfigFile"] is not { Length: > 0 } file)
    {
        return;
    }

    builder.Configuration.AddJsonFile(Path.GetFullPath(file, builder.Environment.ContentRootPath), optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
}
