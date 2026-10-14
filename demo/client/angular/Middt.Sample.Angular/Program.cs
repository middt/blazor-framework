using Microsoft.AspNetCore.DataProtection;
using Middt.Framework.Bff.DependencyInjection;

// Backend-for-frontend of the Angular demo (../app): serves the SPA, the login/logout pages
// and proxies /api and /hubs to Middt.Sample.Api with the session's token – the browser never sees a token.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = SpaWebRoot() });

builder.AddServiceDefaults();
// Own cookie name: the Blazor sample runs on the same host (cookies are not separated by port).
builder.AddMiddtSpaBff(bff => bff.CookieName = "middt.angular.auth");

// With Redis (Aspire / compose) sessions and Data Protection keys are shared and survive restarts.
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redis;
        o.InstanceName = "middt-sample-angular:";
    });
    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis), "middt-sample-angular:dataprotection-keys");
}

var app = builder.Build();

app.UseMiddtSpaBff();

app.Run();

// Published / container: the Angular build is copied to wwwroot. Local runs (dotnet run, Aspire): serve the last build of
// ../app (npx ng build sample) directly, so no copy step is needed.
static string? SpaWebRoot()
{
    var contentRoot = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(contentRoot, "wwwroot", "index.html")))
    {
        return null;
    }

    var dist = Path.GetFullPath(Path.Combine(contentRoot, "..", "app", "dist", "sample", "browser"));
    return Directory.Exists(dist) ? dist : null;
}
