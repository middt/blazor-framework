using System.Net;
using Middt.Framework.Blazor.DependencyInjection;
using Middt.Framework.Tests.Api;
using Middt.Framework.Bff.Security;

namespace Middt.Framework.Tests.Blazor;

public sealed class BlazorHostFixture : IAsyncLifetime
{
    public BlazorTestHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Host = await BlazorTestHost.StartAsync();

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>Cookie (BFF) sign-in/out through the framework's Razor Pages.</summary>
public sealed class BffAuthenticationTests(BlazorHostFixture fixture) : IClassFixture<BlazorHostFixture>
{
    private const string AuthCookie = MiddtBlazorExtensions.AuthCookieName;

    private BlazorTestHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    [Fact]
    public async Task Login_page_renders_a_form_with_an_antiforgery_token()
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.GetAsync("/Account/Login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.Should().Contain("<form method=\"post\"").And.Contain("name=\"__RequestVerificationToken\"").And.Contain("name=\"Input.Password\"");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'").And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected_with_400()
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.PostFormAsync("/Account/Login",
        [
            new("Input.Username", FakeUserStore.Username),
            new("Input.Password", FakeUserStore.Password),
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        SetCookie(response, AuthCookie).Should().BeNull();
    }

    [Fact]
    public async Task Valid_login_redirects_to_the_local_return_url_and_issues_a_hardened_cookie()
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.LoginAsync(returnUrl: "/orders/5?tab=2");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/orders/5?tab=2");

        var cookie = SetCookie(response, AuthCookie);
        cookie.Should().NotBeNull();
        cookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=lax").And.ContainEquivalentOf("path=/");
        cookie.Should().NotContainEquivalentOf("secure", "Development over http uses SameAsRequest");
        cookie.Should().NotContain("eyJ", "the cookie is encrypted and never carries the JWT in clear text");

        var me = await browser.WhoAmIAsync();
        me!.Name.Should().Be(FakeUserStore.Username);
        me.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cookie_carries_user_claims_and_session_id_but_no_jwt_registered_claims()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();

        var me = await browser.WhoAmIAsync();

        me!.ClaimTypes.Should().Contain(System.Security.Claims.ClaimTypes.Sid)
            .And.Contain(System.Security.Claims.ClaimTypes.Name)
            .And.Contain(System.Security.Claims.ClaimTypes.Role)
            .And.Contain(Middt.Framework.Bff.Security.MiddtClaimTypes.SessionId);
        me.ClaimTypes.Should().NotContain(["exp", "nbf", "iat", "iss", "aud", "jti"]);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com/phish")]
    public async Task Non_local_return_url_redirects_to_root(string returnUrl)
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.LoginAsync(returnUrl: returnUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task Already_signed_in_user_visiting_login_with_foreign_return_url_goes_to_root()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();

        using var response = await browser.GetAsync("/Account/Login?returnUrl=%2F%2Fevil.com");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Theory]
    [InlineData(FakeUserStore.Username, "wrong")]
    [InlineData("nobody", FakeUserStore.Password)]
    public async Task Wrong_credentials_render_the_page_with_an_error_and_no_cookie(string username, string password)
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.LoginAsync(username, password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.Should().Contain("role=\"alert\"");
        WebUtility.HtmlDecode(html).Should().Contain("Kullanıcı adı veya şifreniz hatalı.");
        html.Should().NotContain(password == "wrong" ? "value=\"wrong\"" : $"value=\"{FakeUserStore.Password}\"", "the password is never echoed back");
        SetCookie(response, AuthCookie).Should().BeNull();
        browser.CookieValue(AuthCookie).Should().BeNull();
    }

    [Fact]
    public async Task Missing_fields_render_a_validation_error_without_calling_the_api()
    {
        var browser = Host.CreateBrowser();
        var token = await browser.AntiforgeryTokenAsync("/Account/Login");

        using var response = await browser.PostFormAsync("/Account/Login", [new("__RequestVerificationToken", token), new("Input.Username", "x")]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("role=\"alert\"");
        SetCookie(response, AuthCookie).Should().BeNull();
        Host.Tokens.LoginRequests.Should().NotContain(r => r.username == "x");
    }

    [Fact]
    public async Task Anonymous_request_to_a_protected_endpoint_is_redirected_to_login()
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.GetAsync(BlazorTestHost.WhoAmIPath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Every_login_gets_a_new_session_id()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();
        var first = (await browser.WhoAmIAsync())!.SessionId;

        // Login again while holding the existing cookie: the session id must change (no session fixation).
        // The login page redirects signed-in users, so take the (user bound) antiforgery token from the logout form.
        var token = await browser.AntiforgeryTokenAsync("/Account/Logout");
        using (var relogin = await browser.PostFormAsync("/Account/Login",
        [
            new("__RequestVerificationToken", token),
            new("Input.Username", FakeUserStore.Username),
            new("Input.Password", FakeUserStore.Password),
        ]))
        {
            relogin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        var second = (await browser.WhoAmIAsync())!.SessionId;

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task Cookie_works_until_the_server_session_is_removed()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();
        var me = await browser.WhoAmIAsync();
        me.Should().NotBeNull();
        (await browser.WhoAmIAsync()).Should().NotBeNull("the cookie is reusable while the session exists");

        await Host.Store.RemoveAsync(me!.SessionId!, Ct);

        using var response = await browser.GetAsync(BlazorTestHost.WhoAmIPath);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "a cookie without server session is treated as anonymous");
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
        browser.CookieValue(AuthCookie).Should().BeNull("the stale cookie is deleted");
    }

    [Fact]
    public async Task Stolen_cookie_is_useless_after_logout()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();
        var stolen = browser.Cookies.GetCookieHeader(browser.BaseAddress);

        (await browser.LogoutAsync()).Dispose();

        var attacker = Host.CreateBrowser();
        attacker.Cookies.SetCookies(attacker.BaseAddress, stolen.Replace("; ", ",", StringComparison.Ordinal));
        (await attacker.WhoAmIAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Logout_post_revokes_the_refresh_token_removes_the_session_and_expires_the_cookie()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var tokens = await Host.Store.GetAsync(sessionId, touch: false, Ct);
        tokens.Should().NotBeNull();

        using var response = await browser.LogoutAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Account/Login");
        Host.Tokens.RevokeRequests.Should().Contain((tokens!.access_token!, tokens.refresh_token!));
        (await Host.Store.GetAsync(sessionId, touch: false, Ct)).Should().BeNull();
        SetCookie(response, AuthCookie).Should().Contain("expires=Thu, 01 Jan 1970");
        (await browser.WhoAmIAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Logout_get_does_not_sign_out()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();

        using var response = await browser.GetAsync("/Account/Logout");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.WhoAmIAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_post_without_antiforgery_token_is_rejected()
    {
        var browser = Host.CreateBrowser();
        (await browser.LoginAsync()).Dispose();

        using var response = await browser.PostFormAsync("/Account/Logout", []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await browser.WhoAmIAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Session_idle_timeout_ends_the_cookie_session()
    {
        // The fixture host is shared by parallel tests: move time on a dedicated host.
        await using var isolated = await BlazorTestHost.StartAsync();
        var other = isolated.CreateBrowser();
        (await other.LoginAsync()).Dispose();
        (await other.WhoAmIAsync()).Should().NotBeNull();

        isolated.Time.Advance(TimeSpan.FromMinutes(11));

        (await other.WhoAmIAsync()).Should().BeNull("10 minutes without activity end the session");
    }

    [Fact]
    public async Task Requests_keep_the_cookie_session_alive()
    {
        await using var isolated = await BlazorTestHost.StartAsync();
        var browser = isolated.CreateBrowser();
        (await browser.LoginAsync()).Dispose();

        for (var i = 0; i < 3; i++)
        {
            isolated.Time.Advance(TimeSpan.FromMinutes(8));
            (await browser.WhoAmIAsync()).Should().NotBeNull($"activity after {(i + 1) * 8} minutes keeps the session");
        }
    }
}

public sealed class BffProductionTests
{
    [Fact]
    public async Task Auth_cookie_is_secure_over_https_in_Production()
    {
        await using var host = await BlazorTestHost.StartAsync(environment: "Production");
        var browser = host.CreateBrowser("https://app.example.com/");

        using var response = await browser.LoginAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(MiddtBlazorExtensions.AuthCookieName + "=", StringComparison.Ordinal));
        cookie.Should().ContainEquivalentOf("secure").And.ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=lax");
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue();
    }
}

public sealed class BffRateLimitTests
{
    [Fact]
    public async Task Login_posts_are_rate_limited_but_rendering_the_page_is_not()
    {
        await using var host = await BlazorTestHost.StartAsync(configure: o => o.LoginAttemptsPerMinute = 3);
        var browser = host.CreateBrowser();
        var token = await browser.AntiforgeryTokenAsync("/Account/Login");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await browser.PostFormAsync("/Account/Login",
            [
                new("__RequestVerificationToken", token),
                new("Input.Username", FakeUserStore.Username),
                new("Input.Password", "wrong"),
            ]);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);

        for (var i = 0; i < 5; i++)
        {
            using var page = await browser.GetAsync("/Account/Login");
            page.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

/// <summary>BFF against the real in-process API with the real BaseTokenService.</summary>
public sealed class BffEndToEndTests
{
    [Fact]
    public async Task Logout_revokes_the_refresh_token_at_the_api()
    {
        await using var api = await TestApiHost.StartAsync();
        await using var host = await BlazorTestHost.StartAsync(api: api);
        var browser = host.CreateBrowser();

        using (var login = await browser.LoginAsync())
        {
            login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var tokens = (await host.Store.GetAsync(sessionId, touch: false, TestContext.Current.CancellationToken))!;

        (await browser.LogoutAsync()).Dispose();

        using var refresh = await api.RefreshAsync(tokens.access_token, tokens.refresh_token);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_shows_the_apis_generic_message()
    {
        await using var api = await TestApiHost.StartAsync();
        await using var host = await BlazorTestHost.StartAsync(api: api);
        var browser = host.CreateBrowser();

        using var response = await browser.LoginAsync(FakeUserStore.Username, "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Kullanıcı adı veya şifreniz hatalı.");
    }
}
