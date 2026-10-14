using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Oidc;
using Middt.Framework.Bff.Proxy;
using Middt.Framework.Bff.Security;
using Middt.Framework.Client.Security;
using Middt.Framework.Tests.Api;
using Middt.Framework.Tests.Blazor;
using Middt.Framework.Tests.Client;
using Yarp.ReverseProxy.Forwarder;

namespace Middt.Framework.Tests.Bff;

/// <summary>
/// SPA BFF host in OIDC mode (configured through <c>Bff:Oidc</c>) against <see cref="FakeOidcProvider"/>, reached through
/// the backchannel authority; the downstream API is a recording <see cref="FakeHttpHandler"/>.
/// </summary>
public sealed class OidcBffTestHost : IAsyncDisposable
{
    public const string ApiUrl = "http://api.test/";

    /// <summary>Challenges the provider regardless of the current sign-in (re-login on the same browser).</summary>
    public const string ChallengePath = "/test/challenge";

    private readonly string contentDirectory;

    private OidcBffTestHost(WebApplication app, FakeOidcProvider provider, FakeTimeProvider time, FakeHttpHandler api, string contentDirectory)
    {
        App = app;
        Provider = provider;
        Time = time;
        Api = api;
        this.contentDirectory = contentDirectory;
    }

    public WebApplication App { get; }

    public FakeOidcProvider Provider { get; }

    public FakeTimeProvider Time { get; }

    public FakeHttpHandler Api { get; }

    public IServerTokenStore Store => App.Services.GetRequiredService<IServerTokenStore>();

    public static async Task<OidcBffTestHost> StartAsync()
    {
        _ = BlazorTestHost.StaticAssetsManifest.Value;
        var contentDirectory = Path.Combine(Path.GetTempPath(), "middt-oidc-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(contentDirectory, "wwwroot");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), SpaBffTestHost.IndexHtml, TestContext.Current.CancellationToken);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = await FakeOidcProvider.StartAsync(time);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(OidcBffTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRoot,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiSettings:URL"] = ApiUrl,
            ["ApiSettings:Version"] = "1",
            ["GeneralSettings:SessionTimeoutInMinute"] = "20",
            ["GeneralSettings:SessionAbsoluteTimeoutInMinute"] = "60",
            ["Bff:Oidc:Authority"] = FakeOidcProvider.Issuer,
            ["Bff:Oidc:BackchannelAuthority"] = FakeOidcProvider.BackchannelAuthority,
            ["Bff:Oidc:ClientId"] = FakeOidcProvider.ClientId,
            ["Bff:Oidc:ClientSecret"] = FakeOidcProvider.ClientSecret,
        });
        builder.Services.AddSingleton<TimeProvider>(time);

        builder.AddMiddtSpaBff(o => o.LoginAttemptsPerMinute = 1000);
        BlazorTestHost.AddBffApplicationPart(builder.Services);

        // Server-to-server calls of the BFF go into the fake provider (after the rewrite handler).
        builder.Services.AddHttpClient(BffOidc.BackchannelClientName).ConfigurePrimaryHttpMessageHandler(provider.CreateHandler);
        var api = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, new { ok = true }));
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new ForwarderFactory(api));

        var app = builder.Build();
        app.UseMiddtSpaBff();
        app.MapGet(BlazorTestHost.WhoAmIPath, (HttpContext context) => Results.Ok(new BlazorTestHost.WhoAmI(
            context.User.Identity?.Name,
            context.User.FindFirst(MiddtClaimTypes.SessionId)?.Value,
            [.. context.User.Claims.Select(c => c.Type)])))
            .RequireAuthorization();
        app.MapGet(ChallengePath, () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [BffOidc.SchemeName]));

        await app.StartAsync();
        return new OidcBffTestHost(app, provider, time, api, contentDirectory);
    }

    public Browser CreateBrowser() => new(App.GetTestClient(), new Uri("http://localhost/"));

    /// <summary>Runs the whole code flow: /Account/Login → provider authorize → callback. Returns the callback response.</summary>
    public async Task<(HttpResponseMessage Challenge, HttpResponseMessage Callback)> SignInAsync(Browser browser, string returnUrl = "/")
    {
        var challenge = await browser.GetAsync("/Account/Login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var idp = Provider.CreateClient();
        using var authorize = await idp.GetAsync(challenge.Headers.Location, TestContext.Current.CancellationToken);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect, "the fake provider signs the test user in immediately");

        var callback = await browser.GetAsync(authorize.Headers.Location!.PathAndQuery);
        return (challenge, callback);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        await Provider.DisposeAsync();
        try
        {
            Directory.Delete(contentDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp web root.
        }
    }

    private sealed class ForwarderFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }
}

public sealed partial class BffOidcTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> Query(Uri uri) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);

    private static async Task<HttpResponseMessage> ApiAsync(Browser browser, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(BffApiProxy.CsrfHeader, "1");
        return await browser.SendAsync(request);
    }

    [Fact]
    public async Task Login_challenges_the_provider_with_code_flow_pkce_and_query_response_mode()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        using var challenge = await browser.GetAsync("/Account/Login?returnUrl=%2Forders%2F5");

        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = challenge.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(FakeOidcProvider.Issuer + "connect/authorize", "browsers are sent to the public authority");
        var query = Query(location);
        query["client_id"].Should().Be(FakeOidcProvider.ClientId);
        query["response_type"].Should().Be("code");
        // response_mode=query is the default of the code flow: the handler omits it (OIDC Core 3.1.2.1), never form_post.
        query.GetValueOrDefault("response_mode", "query").Should().Be("query");
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().NotBeNullOrEmpty();
        query["redirect_uri"].Should().Be("http://localhost/signin-oidc");
        query["scope"].Split(' ').Should().BeEquivalentTo(["openid", "profile", "email", "roles", "offline_access", "api"]);
        query.Should().ContainKeys("state", "nonce");
        query.Should().NotContainKey("client_secret");
    }

    [Fact]
    public async Task Login_rejects_external_return_urls()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        var (_, callback) = await host.SignInAsync(browser, "//evil.example/");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task Callback_creates_a_server_session_and_a_cookie_without_tokens()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        var (_, callback) = await host.SignInAsync(browser, "/orders/5");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/orders/5");

        var me = await browser.WhoAmIAsync();
        me.Should().NotBeNull();
        me!.Name.Should().Be(FakeOidcProvider.UserName);
        me.ClaimTypes.Should().Contain([MiddtClaimTypes.SessionId, System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Email, System.Security.Claims.ClaimTypes.Role]);

        using var userResponse = await browser.GetAsync("/bff/user");
        var user = (await userResponse.Content.ReadFromJsonAsync<BffUser>(TestApiHost.Json, Ct))!;
        user.Id.Should().Be(FakeOidcProvider.Subject);
        user.Roles.Should().BeEquivalentTo(FakeOidcProvider.Roles);

        var tokens = (await host.Store.GetAsync(me.SessionId!, touch: false, Ct))!;
        tokens.access_token.Should().NotBeNullOrEmpty();
        tokens.refresh_token.Should().NotBeNullOrEmpty();
        OidcTokens.GetIdToken(tokens).Should().NotBeNullOrEmpty();
        tokens.expire_datetime.Should().Be(host.Time.GetUtcNow().AddMinutes(60));

        // The cookie ticket holds claims and the session id only – no tokens.
        var cookie = browser.CookieValue(MiddtBffExtensions.AuthCookieName);
        cookie.Should().NotBeNullOrEmpty();
        var ticket = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat.Unprotect(cookie)!;
        ticket.Properties.Items.Keys.Should().NotContain(k => k.StartsWith(".Token", StringComparison.Ordinal));
        ticket.Principal.Claims.Select(c => c.Value).Should().NotContain([tokens.access_token!, tokens.refresh_token!, OidcTokens.GetIdToken(tokens)!]);

        // Code exchange and userinfo went through the backchannel authority with client authentication and PKCE.
        host.Provider.Hosts.Should().Contain("idp.internal");
        host.Provider.Hosts.Count(h => h == "idp.test").Should().Be(1, "only the browser's authorize request uses the public authority");
        var exchange = host.Provider.TokenRequests.Should().ContainSingle().Subject;
        exchange["grant_type"].Should().Be("authorization_code");
        exchange["code_verifier"].Should().NotBeNullOrEmpty();
        host.Provider.UserInfoCalls.Should().Be(1);
    }

    [Fact]
    public async Task Proxied_api_calls_carry_the_access_token_of_the_provider()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();
        await host.SignInAsync(browser);
        var accessToken = (await host.Store.GetAsync((await browser.WhoAmIAsync())!.SessionId!, touch: false, Ct))!.access_token;

        using var response = await ApiAsync(browser, "/api/v1/items/getall");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Api.Requests.Should().ContainSingle().Which.Bearer.Should().Be(accessToken);
    }

    [Fact]
    public async Task Token_close_to_expiry_is_refreshed_at_the_provider_token_endpoint()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();
        await host.SignInAsync(browser);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var before = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;

        host.Time.Advance(host.Provider.AccessTokenLifetime - TimeSpan.FromSeconds(30));
        using var response = await ApiAsync(browser, "/api/v1/items/getall");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refresh = host.Provider.TokenRequests.Last();
        refresh["grant_type"].Should().Be("refresh_token");
        refresh["refresh_token"].Should().Be(before.refresh_token);
        refresh["client_id"].Should().Be(FakeOidcProvider.ClientId);
        refresh["client_secret"].Should().Be(FakeOidcProvider.ClientSecret);

        var after = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;
        after.access_token.Should().NotBe(before.access_token);
        after.refresh_token.Should().NotBe(before.refresh_token, "the provider rotates refresh tokens");
        after.expire_datetime.Should().Be(before.expire_datetime, "refreshing must not extend the session");
        OidcTokens.GetIdToken(after).Should().NotBeNullOrEmpty();
        host.Api.Requests.Should().ContainSingle().Which.Bearer.Should().Be(after.access_token);
    }

    [Fact]
    public async Task Failed_refresh_ends_the_session()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();
        await host.SignInAsync(browser);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var tokens = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;

        // Revoked at the provider (e.g. by an administrator): the refresh grant fails.
        var tokenService = host.App.Services.GetRequiredService<ITokenService>();
        tokenService.Should().BeOfType<OidcTokenService>();
        await tokenService.RevokeAsync(tokens.access_token!, tokens.refresh_token!, Ct);
        host.Time.Advance(host.Provider.AccessTokenLifetime);

        using var response = await ApiAsync(browser, "/api/v1/items/getall");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Store.GetAsync(sessionId, touch: false, Ct)).Should().BeNull();
        host.Api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_ends_the_provider_session()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();
        await host.SignInAsync(browser);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var tokens = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;

        using var logout = await browser.LogoutAsync();

        // A page that navigates to the end-session endpoint (a 302 after a form post would violate form-action 'self').
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await logout.Content.ReadAsStringAsync(Ct);
        var endSession = new Uri(WebUtility.HtmlDecode(EndSessionLink().Match(html).Groups[1].Value));
        endSession.GetLeftPart(UriPartial.Path).Should().Be(FakeOidcProvider.Issuer + "connect/logout");
        var query = Query(endSession);
        query["id_token_hint"].Should().Be(OidcTokens.GetIdToken(tokens));
        query["post_logout_redirect_uri"].Should().Be("http://localhost/signout-callback-oidc");
        html.Should().Contain("http-equiv=\"refresh\"");

        host.Provider.RevokedTokens.Should().Contain(tokens.refresh_token);
        (await host.Store.GetAsync(sessionId, touch: false, Ct)).Should().BeNull();
        (await browser.WhoAmIAsync()).Should().BeNull("the auth cookie is deleted");

        // Provider → signed-out callback → local start page.
        using var idp = host.Provider.CreateClient();
        using var providerLogout = await idp.GetAsync(endSession, Ct);
        providerLogout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        using var signedOut = await browser.GetAsync(providerLogout.Headers.Location!.PathAndQuery);
        signedOut.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedOut.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task Signing_in_again_ends_the_previous_session()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();
        await host.SignInAsync(browser);
        var first = (await browser.WhoAmIAsync())!.SessionId!;
        var firstTokens = (await host.Store.GetAsync(first, touch: false, Ct))!;

        // Re-login on the same browser (the login page only redirects signed-in users: challenge through a test endpoint).
        using var challenge = await browser.GetAsync(OidcBffTestHost.ChallengePath);
        using var idp = host.Provider.CreateClient();
        using var authorize = await idp.GetAsync(challenge.Headers.Location, Ct);
        using var callback = await browser.GetAsync(authorize.Headers.Location!.PathAndQuery);
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var second = (await browser.WhoAmIAsync())!.SessionId!;
        second.Should().NotBe(first, "every sign-in gets a fresh session id");
        (await host.Store.GetAsync(first, touch: false, Ct)).Should().BeNull("the previous session is ended");
        host.Provider.RevokedTokens.Should().Contain(firstTokens.refresh_token);
    }

    [Fact]
    public async Task Password_login_post_is_disabled()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        using var anonymous = await browser.PostFormAsync("/Account/Login", [new("Input.Username", "alice"), new("Input.Password", "secret")]);
        ((int)anonymous.StatusCode).Should().BeGreaterThanOrEqualTo(400);

        // Even with a valid antiforgery token the handler refuses: credentials are only entered at the provider.
        await host.SignInAsync(browser);
        var token = await browser.AntiforgeryTokenAsync("/Account/Logout");
        using var post = await browser.PostFormAsync("/Account/Login",
            [new("__RequestVerificationToken", token), new("Input.Username", "alice"), new("Input.Password", "secret")]);
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Provider.TokenRequests.Should().ContainSingle("only the code exchange of the OIDC sign-in");
    }

    [Fact]
    public async Task Api_paths_answer_401_instead_of_redirecting_to_the_provider()
    {
        await using var host = await OidcBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        using var api = await ApiAsync(browser, "/api/v1/items/getall");
        using var page = await browser.GetAsync("/orders");

        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        page.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Headers.Location!.AbsolutePath.Should().Be("/Account/Login", "pages go through the login page, which challenges the provider");
    }

    [Fact]
    public async Task Without_oidc_configuration_the_password_login_is_unchanged()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = host.CreateBrowser();

        (await host.App.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(BffOidc.SchemeName)).Should().BeNull();
        host.App.Services.GetRequiredService<MiddtBffOptions>().Oidc.IsEnabled.Should().BeFalse();
        using var page = await browser.GetAsync("/Account/Login");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Ct)).Should().Contain("Input.Password");
        using var login = await browser.LoginAsync();
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [GeneratedRegex("id=\"middt-end-session\" href=\"([^\"]+)\"")]
    private static partial Regex EndSessionLink();
}
