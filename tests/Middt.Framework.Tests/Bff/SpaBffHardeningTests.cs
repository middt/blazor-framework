using System.Net;
using Middt.Framework.Bff.Proxy;
using Middt.Framework.Tests.Api;
using Middt.Framework.Tests.Blazor;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Bff;

/// <summary>
/// Security and performance hardening of the SPA BFF: logout CSRF, token endpoint deny list, login limiter per username,
/// session replacement on re-login, HTML only for signed-in users, cache headers, compression and token store reads.
/// </summary>
public sealed class SpaBffHardeningTests(SpaBffHostFixture fixture) : IClassFixture<SpaBffHostFixture>
{
    private const string HashedScript = "main-ABCD2345.js";

    private SpaBffTestHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Browser> SignedInBrowserAsync(SpaBffTestHost host)
    {
        var browser = host.CreateBrowser();
        using var login = await browser.LoginAsync();
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static async Task<HttpResponseMessage> SendAsync(Browser browser, HttpMethod method, string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await browser.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ApiAsync(Browser browser, HttpMethod method, string path, params (string Name, string Value)[] headers) =>
        SendAsync(browser, method, path, [(BffApiProxy.CsrfHeader, "1"), .. headers]);

    private static int ForwardedCount(SpaBffTestHost host)
    {
        lock (host.Api.Requests)
        {
            return host.Api.Requests.Count;
        }
    }

    // ---- 1. Logout CSRF -------------------------------------------------------------------------------------------

    public static TheoryData<string?, string?, bool> LogoutNavigations => new()
    {
        { "same-origin", null, true },
        { "cross-site", null, false },
        { "same-site", null, false },
        { "none", null, false },                                  // typed URL / bookmark: user confirms
        { "cross-site", "http://localhost/orders", false },       // Fetch Metadata wins over a Referer
        { null, "http://localhost/orders", true },                // browsers without Fetch Metadata
        { null, "http://evil.example/page", false },
        { null, "http://localhost:8080/page", false },
        { null, null, false },
    };

    [Theory]
    [MemberData(nameof(LogoutNavigations))]
    public async Task Logout_page_submits_itself_only_for_same_origin_navigations(string? fetchSite, string? referer, bool autoSubmit)
    {
        var browser = await SignedInBrowserAsync(Host);
        var headers = new List<(string, string)>();
        if (fetchSite is not null)
        {
            headers.Add(("Sec-Fetch-Site", fetchSite));
        }

        if (referer is not null)
        {
            headers.Add(("Referer", referer));
        }

        using var response = await SendAsync(browser, HttpMethod.Get, "/Account/Logout", [.. headers]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.Should().Contain("name=\"__RequestVerificationToken\"", "the confirmation form still works");
        if (autoSubmit)
        {
            html.Should().Contain("data-middt-autosubmit");
        }
        else
        {
            html.Should().NotContain("data-middt-autosubmit", "a link from another site must not log the user out");
        }

        (await browser.WhoAmIAsync()).Should().NotBeNull("a GET never ends the session");
    }

    // ---- 2. Token endpoints are not reachable through the proxy -----------------------------------------------------

    [Theory]
    [InlineData("/api/v1/token/login")]
    [InlineData("/api/v1/Token/RefreshToken")]
    [InlineData("/api/v2/TOKEN/revoke")]
    [InlineData("/api/token")]
    [InlineData("/api/v1//token/login")]
    [InlineData("/api/v1/%74oken/login")]
    public async Task Token_endpoints_are_answered_with_404_and_not_forwarded(string path)
    {
        var browser = await SignedInBrowserAsync(Host);
        var before = ForwardedCount(Host);

        using var response = await ApiAsync(browser, HttpMethod.Post, path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ForwardedCount(Host).Should().Be(before, "raw tokens must never reach browser JavaScript");
    }

    [Theory]
    [InlineData("/api/v1/tokens/list", new[] { "token" }, false)]
    [InlineData("/api/v1/mytoken", new[] { "token" }, false)]
    [InlineData("/api/v1/items/token", new[] { "token" }, true)]
    [InlineData("/api/v1/admin/export/all", new[] { "admin/export" }, true)]
    [InlineData("/api/v1/admin/x/export", new[] { "admin/export" }, false)]
    [InlineData("/api/v1/token/login", new string[0], false)]
    public void Deny_list_matches_whole_segment_sequences(string path, string[] denied, bool expected) =>
        BffApiProxy.IsDenied(new Microsoft.AspNetCore.Http.PathString(path), denied).Should().Be(expected);

    [Fact]
    public async Task Deny_list_is_configurable()
    {
        await using var host = await SpaBffTestHost.StartAsync(o =>
        {
            o.ProxyDeniedPaths.Clear();
            o.ProxyDeniedPaths.Add("admin");
        });
        var browser = await SignedInBrowserAsync(host);

        using (var denied = await ApiAsync(browser, HttpMethod.Get, "/api/v1/admin/users"))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using var allowed = await ApiAsync(browser, HttpMethod.Post, "/api/v1/token/login");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Api.Requests.Should().ContainSingle(r => r.Uri == "/api/v1/token/login");
    }

    // ---- 5. Login limiter per username ------------------------------------------------------------------------------

    [Fact]
    public async Task Failed_login_attempts_are_limited_per_normalised_username_across_ips()
    {
        await using var host = await SpaBffTestHost.StartAsync(o => o.LoginAttemptsPerUsernamePerMinute = 2);
        var attacker = host.CreateBrowser();

        (await attacker.LoginAsync(password: "wrong-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await attacker.LoginAsync(username: " " + FakeUserStore.Username.ToUpperInvariant(), password: "wrong-2")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Third attempt for the same account (another browser, correct password): rejected before the token service.
        using var third = await host.CreateBrowser().LoginAsync();
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        WebUtility.HtmlDecode(await third.Content.ReadAsStringAsync(Ct)).Should().Contain("Çok fazla giriş denemesi");
        host.Tokens.LoginRequests.Should().HaveCount(2);

        // Other accounts are not affected; successful logins never count.
        using var other = await attacker.LoginAsync(username: "bob", password: "x");
        other.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Tokens.LoginRequests.Should().HaveCount(3);
    }

    // ---- 3. Non-ASCII return URLs ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("/ürün/5")]
    [InlineData("/\t/evil.example")]
    public async Task Unsafe_return_url_redirects_to_root_instead_of_failing(string returnUrl)
    {
        await using var host = await SpaBffTestHost.StartAsync();

        using var login = await host.CreateBrowser().LoginAsync(returnUrl: returnUrl);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect, "LocalRedirect must not throw on characters it cannot write into Location");
        login.Headers.Location!.OriginalString.Should().Be("/");
    }

    // ---- 6. Re-login ends the previous server session ---------------------------------------------------------------

    [Fact]
    public async Task Signing_in_again_ends_the_previous_server_session()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = await SignedInBrowserAsync(host);
        var first = (await browser.WhoAmIAsync())!.SessionId!;
        var firstTokens = (await host.Store.GetAsync(first, touch: false, Ct))!;

        // The login page redirects signed-in users; post the form with the antiforgery token of another page.
        var antiforgery = await browser.AntiforgeryTokenAsync("/Account/Logout");
        using var relogin = await browser.PostFormAsync("/Account/Login",
        [
            new("__RequestVerificationToken", antiforgery),
            new("Input.Username", FakeUserStore.Username),
            new("Input.Password", FakeUserStore.Password),
        ]);

        relogin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await host.Store.GetAsync(first, touch: false, Ct)).Should().BeNull("the previous session is removed");
        host.Tokens.RevokeRequests.Should().ContainSingle().Which.Should().Be((firstTokens.access_token!, firstTokens.refresh_token!));
        var second = (await browser.WhoAmIAsync())!.SessionId;
        second.Should().NotBeNull().And.NotBe(first);
        (await host.Store.GetAsync(second!, touch: false, Ct)).Should().NotBeNull();
    }

    // ---- 7. HTML only for signed-in users ---------------------------------------------------------------------------

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/INDEX.HTML")]
    public async Task Html_files_redirect_anonymous_visitors_to_the_login_page(string path)
    {
        using var response = await Host.CreateBrowser().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Html_files_are_served_to_signed_in_users_and_revalidated()
    {
        var browser = await SignedInBrowserAsync(Host);

        using var response = await browser.GetAsync("/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(SpaBffTestHost.IndexHtml);
    }

    // ---- 10./11. Cache headers and compression ----------------------------------------------------------------------

    [Theory]
    [InlineData(HashedScript, true)]
    [InlineData("chunk-Z7Q2XK4M.js", true)]
    [InlineData("media/logo.png", true)]
    [InlineData("favicon.ico", false)]
    [InlineData("assets/config.json", false)]
    [InlineData("main.js", false)]
    public async Task Only_hashed_files_are_cached_as_immutable(string file, bool immutable)
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var path = Path.Combine(host.WebRoot, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "content", Ct);

        using var response = await host.CreateBrowser().GetAsync("/" + file);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "static assets are anonymous");
        var cacheControl = response.Headers.CacheControl!;
        if (immutable)
        {
            cacheControl.ToString().Should().Contain("immutable").And.Contain("max-age=31536000");
        }
        else
        {
            cacheControl.NoCache.Should().BeTrue();
            cacheControl.ToString().Should().NotContain("immutable");
        }
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Scripts_are_compressed_on_the_fly(string encoding)
    {
        await using var host = await SpaBffTestHost.StartAsync();
        await File.WriteAllTextAsync(Path.Combine(host.WebRoot, HashedScript), string.Concat(Enumerable.Repeat("console.log('middt');\n", 500)), Ct);

        using var response = await SendAsync(host.CreateBrowser(), HttpMethod.Get, "/" + HashedScript, ("Accept-Encoding", encoding));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be(encoding);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
    }

    [Fact]
    public async Task Precompressed_files_are_served_when_present()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        byte[] brotli = [1, 2, 3, 4];
        await File.WriteAllTextAsync(Path.Combine(host.WebRoot, HashedScript), "console.log(1);", Ct);
        await File.WriteAllBytesAsync(Path.Combine(host.WebRoot, HashedScript + ".br"), brotli, Ct);
        var browser = host.CreateBrowser();

        using (var compressed = await SendAsync(browser, HttpMethod.Get, "/" + HashedScript, ("Accept-Encoding", "gzip, deflate, br")))
        {
            compressed.StatusCode.Should().Be(HttpStatusCode.OK);
            compressed.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("br");
            compressed.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
            compressed.Headers.Vary.Should().Contain("Accept-Encoding");
            compressed.Headers.CacheControl!.ToString().Should().Contain("immutable");
            (await compressed.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(brotli);
        }

        using var plain = await browser.GetAsync("/" + HashedScript);
        plain.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await plain.Content.ReadAsStringAsync(Ct)).Should().Be("console.log(1);");
    }

    [Fact]
    public async Task Html_is_not_compressed()
    {
        var browser = await SignedInBrowserAsync(Host);

        using var response = await SendAsync(browser, HttpMethod.Get, "/orders", ("Accept-Encoding", "br"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty("BREACH: pages carry antiforgery tokens");
    }

    [Fact]
    public async Task Proxied_api_json_is_compressed_and_accept_encoding_is_forwarded()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        host.ApiResponse = (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Enumerable.Range(0, 200).Select(i => new { id = i, name = "item " + i })));
        var browser = await SignedInBrowserAsync(host);

        using var response = await ApiAsync(browser, HttpMethod.Get, "/api/v1/items", ("Accept-Encoding", "gzip, br"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("br");
        host.Api.Headers.Single().Should().ContainKey("Accept-Encoding", "the API may compress its own responses");
    }

    [Fact]
    public async Task Api_compressed_responses_are_relayed_unchanged()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        byte[] body = [9, 8, 7];
        host.ApiResponse = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentType = new("application/json");
            response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        };
        var browser = await SignedInBrowserAsync(host);

        using var relayed = await ApiAsync(browser, HttpMethod.Get, "/api/v1/items", ("Accept-Encoding", "gzip, br"));

        relayed.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("gzip");
        (await relayed.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(body);
    }

    // ---- 9. One token store read per proxied request ----------------------------------------------------------------

    [Fact]
    public async Task Proxied_request_reads_the_token_store_once()
    {
        await using var host = await SpaBffTestHost.StartAsync();
        var browser = await SignedInBrowserAsync(host);
        (await browser.WhoAmIAsync()).Should().NotBeNull();

        for (var i = 0; i < 3; i++)
        {
            host.ResetStoreReads();
            using var response = await ApiAsync(browser, HttpMethod.Get, $"/api/v1/items?page={i}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            host.StoreReads.Should().BeLessThanOrEqualTo(1, "the cookie validation's entry is reused by the proxy and its transform");
        }

        host.Api.Requests.Should().HaveCount(3);
    }
}
