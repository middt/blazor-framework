using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Security;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Security;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Tests.Api;
using Middt.Framework.Tests.Blazor;
using Middt.Framework.Tests.Client;
using Yarp.ReverseProxy.Forwarder;

namespace Middt.Framework.Tests.Bff;

/// <summary>
/// In-process SPA host built like an application (<c>AddMiddtSpaBff</c> + <c>UseMiddtSpaBff</c>) on a TestServer, with a
/// fake token service and a fake downstream API: YARP's outgoing HTTP client is replaced by a recording
/// <see cref="FakeHttpHandler"/>, so every forwarded request (method, path, headers, bearer, body) can be inspected.
/// </summary>
public sealed class SpaBffTestHost : IAsyncDisposable
{
    public const string ApiUrl = "http://api.test/";

    /// <summary>Page that requires a role the test user does not have.</summary>
    public const string ForbiddenPagePath = "/test/forbidden-page";
    public const string IndexHtml = "<!doctype html><title>spa</title><app-root>spa-shell</app-root>";

    private readonly string contentDirectory;
    private readonly CountingTokenStore store;

    private SpaBffTestHost(WebApplication app, FakeTimeProvider time, FakeTokenService tokens, FakeHttpHandler api, string contentDirectory)
    {
        store = (CountingTokenStore)app.Services.GetRequiredService<IServerTokenStore>();
        App = app;
        Time = time;
        Tokens = tokens;
        Api = api;
        this.contentDirectory = contentDirectory;
    }

    public WebApplication App { get; }

    public FakeTimeProvider Time { get; }

    public FakeTokenService Tokens { get; }

    /// <summary>The fake downstream API (records every forwarded request).</summary>
    public FakeHttpHandler Api { get; }

    /// <summary>Lifetime of the access tokens issued by the fake login and refresh.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Answer of the fake API.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ApiResponse { get; set; } =
        (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, new { ok = true }));

    /// <summary>Answer of the fake token refresh; defaults to a fresh token pair.</summary>
    public Func<TokenRefreshRequestModel, CancellationToken, Task<TokenResponseModel>>? Refresh { get; set; }

    public IServerTokenStore Store => store;

    /// <summary>Number of <see cref="IServerTokenStore.GetAsync"/> calls (one Redis read each in production).</summary>
    public int StoreReads => store.Reads;

    public void ResetStoreReads() => store.ResetReads();

    /// <summary>Web root of the SPA (contains <c>index.html</c>); tests may add files.</summary>
    public string WebRoot => Path.Combine(contentDirectory, "wwwroot");

    public static async Task<SpaBffTestHost> StartAsync(Action<MiddtBffOptions>? configure = null)
    {
        _ = BlazorTestHost.StaticAssetsManifest.Value;
        var contentDirectory = Path.Combine(Path.GetTempPath(), "middt-spa-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(contentDirectory, "wwwroot");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), IndexHtml, TestContext.Current.CancellationToken);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(SpaBffTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRoot,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiSettings:URL"] = ApiUrl,
            ["ApiSettings:Version"] = "1",
            ["GeneralSettings:SessionTimeoutInMinute"] = "10",
            ["GeneralSettings:SessionAbsoluteTimeoutInMinute"] = "60",
        });

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddSingleton<TimeProvider>(time);

        builder.AddMiddtSpaBff(o =>
        {
            o.LoginAttemptsPerMinute = 1000;
            configure?.Invoke(o);
        });
        BlazorTestHost.AddBffApplicationPart(builder.Services);

        SpaBffTestHost? host = null;
        var api = new FakeHttpHandler((request, ct) => host!.ApiResponse(request, ct));
        var tokens = new FakeTokenService((request, ct) => host!.Refresh?.Invoke(request, ct) ?? Task.FromResult(host.IssueToken()))
        {
            Login = request => request.username == FakeUserStore.Username && request.password == FakeUserStore.Password
                ? host!.IssueToken()
                : new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["Kullanıcı adı veya şifreniz hatalı."] },
        };
        builder.Services.AddSingleton<ITokenService>(tokens);
        builder.Services.AddSingleton<IServerTokenStore>(sp => new CountingTokenStore(ActivatorUtilities.CreateInstance<DistributedServerTokenStore>(sp)));
        // Registered after AddReverseProxy (TryAdd): replaces YARP's outgoing HTTP client with the fake API.
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new FakeForwarderHttpClientFactory(api));

        var app = builder.Build();
        app.UseMiddtSpaBff();
        app.MapGet(BlazorTestHost.WhoAmIPath, (HttpContext context) => Results.Ok(new BlazorTestHost.WhoAmI(
            context.User.Identity?.Name,
            context.User.FindFirst(MiddtClaimTypes.SessionId)?.Value,
            [.. context.User.Claims.Select(c => c.Type)])))
            .RequireAuthorization();
        app.MapGet(ForbiddenPagePath, () => Results.Ok()).RequireAuthorization(policy => policy.RequireRole("nobody-has-this-role"));

        await app.StartAsync();
        host = new SpaBffTestHost(app, time, tokens, api, contentDirectory);
        return host;
    }

    public Browser CreateBrowser(string baseAddress = "http://localhost/") => new(App.GetTestClient(), new Uri(baseAddress));

    /// <summary>A real JWT (the BFF reads its expiry and the cookie claims) valid for <see cref="TokenLifetime"/> from the fake now.</summary>
    public TokenResponseModel IssueToken()
    {
        var generator = new JwtTokenGenerator(
            Options.Create(new JwtSettings
            {
                Secret = TestApiHost.Secret,
                Issuer = TestApiHost.Issuer,
                Audience = TestApiHost.Audience,
                Expires = (int)TokenLifetime.TotalSeconds,
            }),
            Time);
        var token = generator.CreateToken(
        [
            new Claim(ClaimTypes.Sid, FakeUserStore.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, FakeUserStore.Username),
            new Claim(ClaimTypes.Role, "1"),
            new Claim(ClaimTypes.Role, "admin"),
        ]);
        token.Result = ResultEnum.Success;
        token.Username = FakeUserStore.Username;
        return token;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            Directory.Delete(contentDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp web root.
        }
    }

    private sealed class CountingTokenStore(IServerTokenStore inner) : IServerTokenStore
    {
        private int reads;

        public int Reads => Volatile.Read(ref reads);

        public void ResetReads() => Interlocked.Exchange(ref reads, 0);

        public Task<SaveTokenResponseModel?> GetAsync(string sessionId, bool touch, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            return inner.GetAsync(sessionId, touch, cancellationToken);
        }

        public Task SetAsync(string sessionId, SaveTokenResponseModel tokens, CancellationToken cancellationToken = default) =>
            inner.SetAsync(sessionId, tokens, cancellationToken);

        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(sessionId, cancellationToken);
    }

    private sealed class FakeForwarderHttpClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }
}
