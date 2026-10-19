using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Identity;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Tests.Identity;

/// <summary>
/// In-process identity provider: SQLite in-memory (connection kept open, shared by the identity and journey tables),
/// ephemeral signing keys and data protection, HTTP issuer <c>http://localhost/</c>, job processor off. Seeds a
/// confidential code-flow client (<c>web</c>, implicit consent), an explicit-consent client (<c>consent-app</c>), a
/// client-credentials client (<c>service</c>), the <c>api</c> scope (audience <c>middt-api</c>) and users <c>alice</c>
/// (roles manager, idp-admin) and <c>bob</c>.
/// </summary>
public sealed class IdentityTestHost : IAsyncDisposable
{
    public const string RedirectUri = "http://localhost/signin-oidc";
    public const string PostLogoutRedirectUri = "http://localhost/signout-callback-oidc";
    public const string WebSecret = "web-secret-1";
    public const string ServiceSecret = "service-secret-1";
    public const string AlicePassword = "Alice-Pass1!";
    public const string BobPassword = "Bob-Pass1!";

    private readonly bool ownsConnection;

    private IdentityTestHost(WebApplication app, SqliteConnection connection, bool ownsConnection)
    {
        App = app;
        Connection = connection;
        this.ownsConnection = ownsConnection;
    }

    public WebApplication App { get; }

    public IServiceProvider Services => App.Services;

    public SqliteConnection Connection { get; }

    public DevelopmentMailbox Mailbox => Services.GetRequiredService<DevelopmentMailbox>();

    public static async Task<IdentityTestHost> StartAsync(Action<MiddtIdentityOptions>? configure = null, SqliteConnection? connection = null)
    {
        var ownsConnection = connection is null;
        if (connection is null)
        {
            connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(IdentityTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        var sqlite = connection;
        builder.AddMiddtIdentityServer(o =>
        {
            o.Issuer = "http://localhost/";
            o.UseDbContext(db => db.UseSqlite(sqlite));
            o.AutoCreateSchema = true;
            o.RequireHttps = false;
            o.UseEphemeralKeys();
            o.ConfigureWorkflow(w => w.EnableJobProcessor = false);
            o.Seed.Roles.Add("manager");
            o.Seed.Scopes.Add(new MiddtIdentitySeedScope { Name = "api", DisplayName = "Demo API", Resources = ["middt-api"] });
            o.Seed.Clients.Add(new MiddtIdentitySeedClient
            {
                ClientId = "web",
                ClientSecret = WebSecret,
                DisplayName = "Web uygulaması",
                RedirectUris = [RedirectUri],
                PostLogoutRedirectUris = [PostLogoutRedirectUri],
                Scopes = ["openid", "profile", "email", "roles", "offline_access", "api"],
            });
            o.Seed.Clients.Add(new MiddtIdentitySeedClient
            {
                ClientId = "consent-app",
                ClientSecret = "consent-secret",
                DisplayName = "Üçüncü taraf uygulama",
                ConsentType = "explicit",
                RedirectUris = [RedirectUri],
                Scopes = ["openid", "profile", "email", "api"],
            });
            o.Seed.Clients.Add(new MiddtIdentitySeedClient
            {
                ClientId = "service",
                ClientSecret = ServiceSecret,
                DisplayName = "Arka plan servisi",
                GrantTypes = ["client_credentials"],
                Scopes = ["api"],
            });
            o.Seed.Users.Add(new MiddtIdentitySeedUser
            {
                UserName = "alice", Email = "alice@test.com", Password = AlicePassword, DisplayName = "Alice Admin", Roles = ["manager", "idp-admin"],
            });
            o.Seed.Users.Add(new MiddtIdentitySeedUser { UserName = "bob", Email = "bob@test.com", Password = BobPassword });
            configure?.Invoke(o);
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMiddtIdentity();

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }

            throw;
        }

        return new IdentityTestHost(app, connection, ownsConnection);
    }

    /// <summary>A browser: own cookie jar, redirects are not followed.</summary>
    public Browser NewBrowser() => new(App.GetTestServer());

    public async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<T> JourneysAsync<T>(Func<IAuthJourneyService, Task<T>> action) => WithServiceAsync(action);

    public Task<T> UsersAsync<T>(Func<UserManager<MiddtIdentityUser>, Task<T>> action) => WithServiceAsync(action);

    public Task<T> WorkflowDbAsync<T>(Func<WorkflowDbContext, Task<T>> action) => WithServiceAsync(action);

    /// <summary>Every text column of the journey (workflow) tables, to prove secrets are never persisted.</summary>
    public Task<string> DumpJourneyTablesAsync() => WorkflowDbAsync(async db =>
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new StringBuilder();
        foreach (var i in await db.Instances.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct))
        {
            builder.AppendLine(i.PendingInput).AppendLine(i.Error);
        }

        foreach (var d in await db.InstanceData.AsNoTracking().ToListAsync(ct))
        {
            builder.AppendLine(d.Data);
        }

        foreach (var h in await db.History.AsNoTracking().ToListAsync(ct))
        {
            builder.AppendLine(h.Input).AppendLine(h.Data);
        }

        foreach (var j in await db.Jobs.AsNoTracking().ToListAsync(ct))
        {
            builder.AppendLine(j.Payload).AppendLine(j.LastError);
        }

        foreach (var incident in await db.Incidents.AsNoTracking().ToListAsync(ct))
        {
            builder.AppendLine(incident.Error);
        }

        return builder.ToString();
    });

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}

/// <summary>Minimal browser over the TestServer: cookie jar (paths honoured), no automatic redirects.</summary>
public sealed class Browser : IDisposable
{
    private static readonly Uri Base = new("http://localhost/");

    private readonly HttpClient client;

    public Browser(TestServer server)
    {
        Cookies = new CookieContainer();
        client = new HttpClient(new CookieHandler(Cookies) { InnerHandler = server.CreateHandler() }) { BaseAddress = Base };
    }

    public CookieContainer Cookies { get; }

    public HttpClient Http => client;

    public Task<HttpResponseMessage> GetAsync(string url) => client.GetAsync(url, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, AuthenticationHeaderValue? authorization = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = authorization;
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The binding secret of the journey cookie for <paramref name="id"/> (as the UI would read it).</summary>
    public string JourneyBinding(Guid id)
    {
        var value = Cookies.GetCookies(new Uri(Base, "/account/journey"))[MiddtIdentityDefaults.JourneyCookieName]?.Value
            ?? throw new InvalidOperationException("No journey cookie.");
        value.Should().StartWith(id.ToString("N") + ".");
        return value[(value.IndexOf('.', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>Sets the journey cookie like the UI does after <see cref="IAuthJourneyService.StartAsync"/>.</summary>
    public void SetJourneyCookie(AuthJourneyHandle handle) =>
        Cookies.Add(Base, new Cookie(MiddtIdentityDefaults.JourneyCookieName, $"{handle.Id:N}.{handle.Binding}", "/account"));

    public bool HasSessionCookie => Cookies.GetCookies(Base)[MiddtIdentityDefaults.CookieName] is { Expired: false, Value.Length: > 0 };

    public void Dispose() => client.Dispose();

    private sealed class CookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (cookies.GetCookieHeader(uri) is { Length: > 0 } header)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}

/// <summary>PKCE pair and OIDC helpers for the tests.</summary>
public static class Oidc
{
    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string AuthorizeUrl(string challenge, string clientId = "web", string scope = "openid profile email roles offline_access api",
        string redirectUri = IdentityTestHost.RedirectUri, string? extra = null) =>
        $"/connect/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + $"&scope={Uri.EscapeDataString(scope)}&state=st-123&nonce=n-456&code_challenge={challenge}&code_challenge_method=S256{extra}";

    public static Dictionary<string, string> Query(Uri location) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query).ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);

    /// <summary>Location of a redirect, resolved against the test host.</summary>
    public static Uri Location(HttpResponseMessage response)
    {
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.SeeOther, HttpStatusCode.MovedPermanently);
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location : new Uri(new Uri("http://localhost/"), location);
    }

    public static Guid JourneyId(Uri location)
    {
        location.AbsolutePath.Should().StartWith("/account/journey/");
        return Guid.Parse(location.AbsolutePath["/account/journey/".Length..]);
    }

    public static JsonObject Payload(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3, "access tokens are signed JWTs, not encrypted (JWE has 5 parts)");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonNode.Parse(Convert.FromBase64String(payload))!.AsObject();
    }

    public static async Task<JsonObject> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static JsonObject Input(object value) => JsonSerializer.SerializeToNode(value, value.GetType(), JsonSerializerOptions.Web)!.AsObject();
}
