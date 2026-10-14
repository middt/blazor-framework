using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Middt.Framework.Bff.DependencyInjection;
using Middt.Framework.Bff.Proxy;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Tests.Api;
using Middt.Framework.Tests.Blazor;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Bff;

public sealed class SpaBffHostFixture : IAsyncLifetime
{
    public SpaBffTestHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Host = await SpaBffTestHost.StartAsync();

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>SPA host (<c>AddMiddtSpaBff</c> + <c>UseMiddtSpaBff</c>): /bff/user, the /api proxy, CSRF, token refresh and SPA fallback.</summary>
public sealed class SpaBffTests(SpaBffHostFixture fixture) : IClassFixture<SpaBffHostFixture>
{
    private SpaBffTestHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Browser> SignedInBrowserAsync(SpaBffTestHost? host = null)
    {
        var browser = (host ?? Host).CreateBrowser();
        using var login = await browser.LoginAsync();
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static async Task<HttpResponseMessage> ApiAsync(Browser browser, HttpMethod method, string path, bool csrf = true, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        if (csrf)
        {
            request.Headers.Add(BffApiProxy.CsrfHeader, "1");
        }

        return await browser.SendAsync(request);
    }

    private static async Task<BffUser> UserAsync(Browser browser)
    {
        using var response = await browser.GetAsync("/bff/user");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BffUser>(TestApiHost.Json, Ct))!;
    }

    private static int ForwardedCount(SpaBffTestHost host)
    {
        lock (host.Api.Requests)
        {
            return host.Api.Requests.Count;
        }
    }

    [Fact]
    public async Task Bff_user_of_an_anonymous_browser_is_not_authenticated()
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.GetAsync("/bff/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = (await response.Content.ReadFromJsonAsync<BffUser>(TestApiHost.Json, Ct))!;
        user.IsAuthenticated.Should().BeFalse();
        user.Id.Should().BeNull();
        user.Name.Should().BeNull();
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task Bff_user_of_a_signed_in_browser_has_name_id_and_roles_and_is_not_cacheable()
    {
        var browser = await SignedInBrowserAsync();

        using var response = await browser.GetAsync("/bff/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("the user's identity must not be cached by the browser or proxies");
        var user = (await response.Content.ReadFromJsonAsync<BffUser>(TestApiHost.Json, Ct))!;
        user.IsAuthenticated.Should().BeTrue();
        user.Name.Should().Be(FakeUserStore.Username);
        user.Id.Should().Be(FakeUserStore.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        user.Roles.Should().BeEquivalentTo("1", "admin");
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("eyJ", "tokens never reach the browser");
    }

    [Theory]
    [InlineData("/api/items")]
    [InlineData("/hubs/notifications/negotiate")]
    public async Task Anonymous_api_call_gets_401_not_a_login_redirect(string path)
    {
        var browser = Host.CreateBrowser();
        var before = ForwardedCount(Host);

        using var response = await ApiAsync(browser, HttpMethod.Get, path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        ForwardedCount(Host).Should().Be(before);
    }

    [Fact]
    public async Task Signed_in_user_without_the_role_gets_the_access_denied_page_not_the_login_page()
    {
        // With an identity provider the login page signs in silently: redirecting there would loop.
        var browser = await SignedInBrowserAsync();

        using var response = await browser.GetAsync(SpaBffTestHost.ForbiddenPagePath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("http://localhost/Account/AccessDenied");

        using var page = await browser.GetAsync("/Account/AccessDenied");
        page.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await page.Content.ReadAsStringAsync(Ct)).Should().Contain("Erişim engellendi");
    }

    [Fact]
    public async Task Api_call_without_csrf_header_is_rejected_with_403()
    {
        var browser = await SignedInBrowserAsync();
        var before = ForwardedCount(Host);

        using var response = await ApiAsync(browser, HttpMethod.Post, "/api/items", csrf: false, content: JsonContent.Create(new { name = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ForwardedCount(Host).Should().Be(before, "a cross-site request must not reach the API");
    }

    [Fact]
    public async Task Api_call_is_forwarded_with_the_session_token_without_browser_cookie_and_csrf_header()
    {
        var browser = await SignedInBrowserAsync();
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var accessToken = (await Host.Store.GetAsync(sessionId, touch: false, Ct))!.access_token;
        const string body = """{"name":"bike","price":12.5}""";
        const string path = "/api/v1/items/5?filter=a%20b&sort=name";

        using var response = await ApiAsync(browser, HttpMethod.Put, path, content: new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("\"ok\":true");

        (HttpMethod Method, string Uri, string? Bearer, string? Body) forwarded;
        Dictionary<string, string> headers;
        lock (Host.Api.Requests)
        {
            var index = Host.Api.Requests.FindLastIndex(r => r.Uri == path);
            index.Should().BeGreaterThanOrEqualTo(0, "the request is forwarded with path and query preserved");
            forwarded = Host.Api.Requests[index];
            headers = Host.Api.Headers[index];
        }

        var (method, _, bearer, forwardedBody) = forwarded;
        method.Should().Be(HttpMethod.Put);
        bearer.Should().Be(accessToken);
        forwardedBody.Should().Be(body);
        headers.Should().NotContainKey("Cookie", "browser cookies are not forwarded to the API");
        headers.Should().NotContainKey(BffApiProxy.CsrfHeader);
    }

    [Fact]
    public async Task Api_status_and_body_are_relayed_to_the_browser()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        host.ApiResponse = (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Created, new { id = 42 }));
        var browser = await SignedInBrowserAsync(host);

        using var response = await ApiAsync(browser, HttpMethod.Post, "/api/items", content: JsonContent.Create(new { name = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("\"id\":42");
        host.Api.Requests.Single().Body.Should().Contain("\"name\":\"x\"");
    }

    [Fact]
    public async Task Token_close_to_expiry_is_refreshed_once_for_concurrent_requests()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = await SignedInBrowserAsync(host);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var original = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TokenResponseModel? refreshed = null;
        host.Refresh = async (_, _) =>
        {
            await release.Task;
            refreshed = host.IssueToken();
            refreshed.refresh_token = "rotated";
            return refreshed;
        };

        // 9:10 into a 10 minute token: less than 60 s left.
        host.Time.Advance(TimeSpan.FromSeconds(550));

        var calls = Enumerable.Range(0, 6).Select(i => ApiAsync(browser, HttpMethod.Get, $"/api/items?page={i}")).ToList();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (host.Tokens.RefreshCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        await Task.Delay(100, Ct); // let the other requests pile up on the pending refresh
        release.SetResult();
        var responses = await Task.WhenAll(calls);

        try
        {
            responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        host.Tokens.RefreshCalls.Should().Be(1, "refresh tokens are single use: concurrent requests share one refresh");
        host.Tokens.RefreshRequests.Single().refresh_token.Should().Be(original.refresh_token);
        host.Tokens.RefreshRequests.Single().token.Should().Be(original.access_token);
        host.Api.Requests.Should().HaveCount(6).And.OnlyContain(r => r.Bearer == refreshed!.access_token);

        var stored = (await host.Store.GetAsync(sessionId, touch: false, Ct))!;
        stored.access_token.Should().Be(refreshed!.access_token);
        stored.refresh_token.Should().Be("rotated");
        stored.expire_datetime.Should().Be(original.expire_datetime, "refreshing tokens must not extend the absolute session expiry");

        // The new token is used without another refresh.
        using var next = await ApiAsync(browser, HttpMethod.Get, "/api/items");
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Tokens.RefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task Token_with_time_left_is_not_refreshed()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = await SignedInBrowserAsync(host);
        host.Time.Advance(TimeSpan.FromSeconds(500));

        using var response = await ApiAsync(browser, HttpMethod.Get, "/api/items");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Tokens.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task Failed_refresh_answers_401_and_ends_the_session()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = await SignedInBrowserAsync(host);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        host.Refresh = (_, _) => Task.FromResult(new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["revoked"] });
        host.Time.Advance(TimeSpan.FromSeconds(550));

        using var response = await ApiAsync(browser, HttpMethod.Get, "/api/items");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        host.Api.Requests.Should().BeEmpty("nothing is forwarded without a valid token");
        (await host.Store.GetAsync(sessionId, touch: false, Ct)).Should().BeNull("the session is removed");
        (await UserAsync(browser)).IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/orders/5")]
    public async Task Anonymous_navigation_is_redirected_to_the_login_page(string path)
    {
        var browser = Host.CreateBrowser();

        using var response = await browser.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("http://localhost/Account/Login?ReturnUrl=" + Uri.EscapeDataString(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/orders/5")]
    public async Task Signed_in_navigation_serves_index_html_that_is_always_revalidated(string path)
    {
        var browser = await SignedInBrowserAsync();

        using var response = await browser.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(SpaBffTestHost.IndexHtml);
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'");
    }

    [Fact]
    public async Task Login_page_renders_and_login_post_signs_in()
    {
        var browser = Host.CreateBrowser();

        using (var page = await browser.GetAsync("/Account/Login"))
        {
            page.StatusCode.Should().Be(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(Ct)).Should().Contain("<form method=\"post\"").And.Contain("name=\"Input.Password\"");
        }

        using var login = await browser.LoginAsync(returnUrl: "/orders/5");

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().Be("/orders/5");
        browser.CookieValue(MiddtBffExtensions.AuthCookieName).Should().NotBeNullOrEmpty();
        (await UserAsync(browser)).IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_ends_api_access()
    {
        var browser = await SignedInBrowserAsync();
        (await browser.LogoutAsync()).Dispose();

        using var response = await ApiAsync(browser, HttpMethod.Get, "/api/items");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Custom_cookie_name_is_used()
    {
        await using var host = await SpaBffTestHost.StartAsync(o => o.CookieName = "shop.auth");
        var browser = host.CreateBrowser();

        using var login = await browser.LoginAsync();

        login.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("shop.auth=", StringComparison.Ordinal))
            .And.NotContain(c => c.StartsWith(MiddtBffExtensions.AuthCookieName + "=", StringComparison.Ordinal));
        browser.CookieValue("shop.auth").Should().NotBeNullOrEmpty();
        (await UserAsync(browser)).IsAuthenticated.Should().BeTrue();
        using var api = await ApiAsync(browser, HttpMethod.Get, "/api/items");
        api.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:8080")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task WebSocket_upgrade_from_a_foreign_or_missing_origin_is_rejected(string? origin)
    {
        var browser = await SignedInBrowserAsync();
        var before = ForwardedCount(Host);
        var client = Host.App.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Cookie = browser.Cookies.GetCookieHeader(browser.BaseAddress);
            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }
        };

        var connect = () => client.ConnectAsync(new Uri("ws://localhost/hubs/notifications"), Ct);

        (await connect.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("403");
        ForwardedCount(Host).Should().Be(before);
    }

    [Fact]
    public async Task WebSocket_upgrade_from_the_same_origin_is_forwarded_with_the_session_token()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        // The fake API does not speak WebSocket: it answers the forwarded handshake with 404.
        host.ApiResponse = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var browser = await SignedInBrowserAsync(host);
        var sessionId = (await browser.WhoAmIAsync())!.SessionId!;
        var accessToken = (await host.Store.GetAsync(sessionId, touch: false, Ct))!.access_token;
        var client = host.App.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Cookie = browser.Cookies.GetCookieHeader(browser.BaseAddress);
            request.Headers.Origin = "http://localhost";
        };

        var connect = () => client.ConnectAsync(new Uri("ws://localhost/hubs/notifications?id=1"), Ct);

        (await connect.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().NotContain("403");
        var forwarded = host.Api.Requests.Should().ContainSingle().Subject;
        forwarded.Uri.Should().Be("/hubs/notifications?id=1");
        forwarded.Bearer.Should().Be(accessToken);
        host.Api.Headers.Single().Should().NotContainKey("Cookie");
    }
}
