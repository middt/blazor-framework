using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Middt.Identity;

namespace Middt.Framework.Tests.Identity;

/// <summary>OAuth 2.0 / OpenID Connect protocol: authorization code + PKCE, refresh, revocation, userinfo, client credentials, end session.</summary>
public sealed class IdentityProtocolTests
{
    private static AuthenticationHeaderValue Basic(string clientId, string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(secret)}")));

    /// <summary>authorize → login journey → credentials → complete → authorize again → redirect with the code.</summary>
    internal static async Task<Uri> SignInThroughJourneyAsync(IdentityTestHost host, Browser browser, string authorizeUrl, string userName, string password)
    {
        var journey = Oidc.Location(await browser.GetAsync(authorizeUrl));
        var id = Oidc.JourneyId(journey);
        var binding = browser.JourneyBinding(id);

        var state = await host.JourneysAsync(j => j.SubmitAsync(id, binding, "submit-credentials", Oidc.Input(new { userName, password }), TestContext.Current.CancellationToken));
        state!.IsSucceeded.Should().BeTrue(string.Join(" ", state.Errors));

        var back = Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id)));
        back.AbsolutePath.Should().Be("/connect/authorize");
        browser.HasSessionCookie.Should().BeTrue();
        return Oidc.Location(await browser.GetAsync(back.PathAndQuery));
    }

    internal static async Task<HttpResponseMessage> RedeemAsync(Browser browser, string code, string verifier, string clientId = "web", string secret = IdentityTestHost.WebSecret) =>
        await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = IdentityTestHost.RedirectUri,
            ["code_verifier"] = verifier,
        }, Basic(clientId, secret));

    [Fact]
    public async Task Authorization_code_flow_with_pkce_end_to_end()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();

        // 1. not signed in: the authorize endpoint starts the login journey bound to this browser
        var first = await browser.GetAsync(Oidc.AuthorizeUrl(challenge));
        var journeyId = Oidc.JourneyId(Oidc.Location(first));
        var binding = browser.JourneyBinding(journeyId);
        var state = await host.JourneysAsync(j => j.GetAsync(journeyId, binding, TestContext.Current.CancellationToken));
        state!.View.Should().Be("credentials");
        state.ReturnUrl.Should().StartWith("/connect/authorize?client_id=web");
        state.Actions.Select(a => a.Key).Should().Contain(["submit-credentials", "register", "forgot-password"]);
        state.Actions.Single(a => a.Key == "submit-credentials").InputSchema!["properties"]!["password"]!["title"]!.GetValue<string>().Should().Be("Şifre");

        // 2. credentials → authenticated → complete (signs into the IdP cookie) → back to authorize → code
        state = await host.JourneysAsync(j => j.SubmitAsync(journeyId, binding, "submit-credentials",
            Oidc.Input(new { userName = "alice", password = IdentityTestHost.AlicePassword }), TestContext.Current.CancellationToken));
        state!.State.Should().Be("authenticated");
        state.IsSucceeded.Should().BeTrue();
        state.Data["userName"]!.GetValue<string>().Should().Be("alice");

        var back = Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(journeyId)));
        back.AbsolutePath.Should().Be("/connect/authorize");
        browser.HasSessionCookie.Should().BeTrue();
        browser.Cookies.GetCookies(new Uri("http://localhost/account/"))[MiddtIdentityDefaults.JourneyCookieName].Should().BeNull("the journey cookie is deleted");

        var callback = Oidc.Location(await browser.GetAsync(back.PathAndQuery));
        callback.GetLeftPart(UriPartial.Path).Should().Be(IdentityTestHost.RedirectUri);
        var query = Oidc.Query(callback);
        query["state"].Should().Be("st-123");
        var code = query["code"];

        // 3. token
        var tokenResponse = await RedeemAsync(browser, code, verifier);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tokenResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tokens = await Oidc.JsonAsync(tokenResponse);
        var access = Oidc.Payload(tokens["access_token"]!.GetValue<string>());
        access["iss"]!.GetValue<string>().Should().Be("http://localhost/");
        access["name"]!.GetValue<string>().Should().Be("Alice Admin");
        access["email"]!.GetValue<string>().Should().Be("alice@test.com");
        access["role"]!.AsArray().Select(r => r!.GetValue<string>()).Should().BeEquivalentTo("manager", "idp-admin");
        access["aud"]!.ToJsonString().Should().Contain("middt-api");
        var identity = Oidc.Payload(tokens["id_token"]!.GetValue<string>());
        identity["sub"]!.GetValue<string>().Should().Be(access["sub"]!.GetValue<string>());
        identity["nonce"]!.GetValue<string>().Should().Be("n-456");
        identity["email"]!.GetValue<string>().Should().Be("alice@test.com");
        identity["auth_time"]!.GetValue<long>().Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 60);
        access.ContainsKey("auth_time").Should().BeFalse();
        var refresh = tokens["refresh_token"]!.GetValue<string>();

        // 4. userinfo
        var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["access_token"]!.GetValue<string>());
        var userInfoResponse = await browser.Http.SendAsync(userInfoRequest, TestContext.Current.CancellationToken);
        userInfoResponse.StatusCode.Should().Be(HttpStatusCode.OK, userInfoResponse.Headers.ToString());
        var userInfo = await Oidc.JsonAsync(userInfoResponse);
        userInfo["sub"]!.GetValue<string>().Should().Be(access["sub"]!.GetValue<string>());
        userInfo["preferred_username"]!.GetValue<string>().Should().Be("alice");
        userInfo["email_verified"]!.GetValue<bool>().Should().BeTrue();
        userInfo["role"]!.AsArray().Should().HaveCount(2);

        // 5. refresh (rotation: a new refresh token is issued)
        var refreshed = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
        }, Basic("web", IdentityTestHost.WebSecret));
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await Oidc.JsonAsync(refreshed))["refresh_token"]!.GetValue<string>();
        rotated.Should().NotBe(refresh);

        // 6. introspection + revocation
        var introspection = await Oidc.JsonAsync(await browser.PostFormAsync("/connect/introspect",
            new Dictionary<string, string> { ["token"] = rotated }, Basic("web", IdentityTestHost.WebSecret)));
        introspection["active"]!.GetValue<bool>().Should().BeTrue();

        (await browser.PostFormAsync("/connect/revoke", new Dictionary<string, string> { ["token"] = rotated },
            Basic("web", IdentityTestHost.WebSecret))).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterRevoke = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = rotated,
        }, Basic("web", IdentityTestHost.WebSecret));
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Oidc.JsonAsync(afterRevoke))["error"]!.GetValue<string>().Should().Be("invalid_grant");

        // the code is single use (a replay also revokes what was issued from it)
        (await RedeemAsync(browser, code, verifier)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 7. a second authorization request reuses the IdP session: no journey
        var (_, challenge2) = Oidc.NewPkce();
        var direct = Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge2)));
        direct.GetLeftPart(UriPartial.Path).Should().Be(IdentityTestHost.RedirectUri);
        Oidc.Query(direct).Should().ContainKey("code");
    }

    [Fact]
    public async Task Discovery_document_lists_the_endpoints()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();

        var discovery = await Oidc.JsonAsync(await browser.GetAsync("/.well-known/openid-configuration"));

        discovery["issuer"]!.GetValue<string>().Should().Be("http://localhost/");
        discovery["authorization_endpoint"]!.GetValue<string>().Should().Be("http://localhost/connect/authorize");
        discovery["end_session_endpoint"]!.GetValue<string>().Should().Be("http://localhost/connect/logout");
        discovery["userinfo_endpoint"]!.GetValue<string>().Should().Be("http://localhost/connect/userinfo");
        discovery["revocation_endpoint"].Should().NotBeNull();
        discovery["introspection_endpoint"].Should().NotBeNull();
        discovery["code_challenge_methods_supported"]!.ToJsonString().Should().Contain("S256");
        var keys = await Oidc.JsonAsync(await browser.GetAsync(new Uri(discovery["jwks_uri"]!.GetValue<string>()).PathAndQuery));
        keys["keys"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Refresh_is_rejected_when_the_user_is_locked_out_or_deleted()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();
        var callback = await SignInThroughJourneyAsync(host, browser, Oidc.AuthorizeUrl(challenge), "bob", IdentityTestHost.BobPassword);
        var tokens = await Oidc.JsonAsync(await RedeemAsync(browser, Oidc.Query(callback)["code"], verifier));

        await host.UsersAsync(async users =>
        {
            var bob = (await users.FindByNameAsync("bob"))!;
            return await users.SetLockoutEndDateAsync(bob, DateTimeOffset.UtcNow.AddHours(1));
        });

        var refreshed = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens["refresh_token"]!.GetValue<string>(),
        }, Basic("web", IdentityTestHost.WebSecret));
        refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Oidc.JsonAsync(refreshed))["error"]!.GetValue<string>().Should().Be("invalid_grant");

        // the locked out user's IdP session is not accepted either: a new login journey starts
        var (_, challenge2) = Oidc.NewPkce();
        Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge2))).AbsolutePath.Should().StartWith("/account/journey/");
    }

    [Fact]
    public async Task Client_credentials_issue_an_api_token_without_user()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();

        var response = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "api",
        }, Basic("service", IdentityTestHost.ServiceSecret));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await Oidc.JsonAsync(response);
        tokens.ContainsKey("refresh_token").Should().BeFalse();
        var access = Oidc.Payload(tokens["access_token"]!.GetValue<string>());
        access["sub"]!.GetValue<string>().Should().Be("service");
        access["aud"]!.ToJsonString().Should().Contain("middt-api");

        var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["access_token"]!.GetValue<string>());
        (await browser.Http.SendAsync(userInfoRequest, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongSecret = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "api",
        }, Basic("service", "wrong"));
        wrongSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var codeClient = await browser.PostFormAsync("/connect/token", new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
            Basic("web", IdentityTestHost.WebSecret));
        codeClient.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the web client has no client_credentials permission");
    }

    [Fact]
    public async Task End_session_signs_out_and_redirects_to_the_registered_post_logout_uri()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();
        var callback = await SignInThroughJourneyAsync(host, browser, Oidc.AuthorizeUrl(challenge), "alice", IdentityTestHost.AlicePassword);
        var tokens = await Oidc.JsonAsync(await RedeemAsync(browser, Oidc.Query(callback)["code"], verifier));

        var logout = Oidc.Location(await browser.GetAsync("/connect/logout?id_token_hint=" + tokens["id_token"]!.GetValue<string>()
            + "&post_logout_redirect_uri=" + Uri.EscapeDataString(IdentityTestHost.PostLogoutRedirectUri) + "&state=bye"));

        logout.GetLeftPart(UriPartial.Path).Should().Be(IdentityTestHost.PostLogoutRedirectUri);
        Oidc.Query(logout)["state"].Should().Be("bye");
        browser.HasSessionCookie.Should().BeFalse();
        var (_, challenge2) = Oidc.NewPkce();
        Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge2))).AbsolutePath.Should().StartWith("/account/journey/");

        // an unregistered post logout URI is not followed
        var other = await browser.GetAsync("/connect/logout?post_logout_redirect_uri=" + Uri.EscapeDataString("http://evil.example/"));
        other.Headers.Location?.ToString().Should().NotContain("evil.example");
    }

    [Fact]
    public async Task Invalid_redirect_uri_and_missing_or_wrong_pkce_are_rejected()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();

        // unregistered redirect URI: an error page, never a redirect to the attacker
        var invalidRedirect = await browser.GetAsync(Oidc.AuthorizeUrl(challenge, redirectUri: "http://evil.example/cb"));
        invalidRedirect.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidRedirect.Headers.Location.Should().BeNull();

        // no code_challenge: rejected (PKCE is required for every client), no journey is started
        var noPkce = await browser.GetAsync($"/connect/authorize?client_id=web&response_type=code&redirect_uri={Uri.EscapeDataString(IdentityTestHost.RedirectUri)}&scope=openid&state=s1");
        noPkce.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        browser.Cookies.GetCookies(new Uri("http://localhost/account/"))[MiddtIdentityDefaults.JourneyCookieName].Should().BeNull();

        // prompt=none without a session
        var none = Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, extra: "&prompt=none")));
        Oidc.Query(none)["error"].Should().Be("login_required");

        // wrong code_verifier
        var callback = await SignInThroughJourneyAsync(host, browser, Oidc.AuthorizeUrl(challenge), "alice", IdentityTestHost.AlicePassword);
        var (otherVerifier, _) = Oidc.NewPkce();
        var wrong = await RedeemAsync(browser, Oidc.Query(callback)["code"], otherVerifier);
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Oidc.JsonAsync(wrong))["error"]!.GetValue<string>().Should().Be("invalid_grant");
        verifier.Should().NotBe(otherVerifier);
    }

    [Fact]
    public async Task Prompt_login_forces_a_new_journey_and_returns_without_the_prompt()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (_, challenge) = Oidc.NewPkce();
        await SignInThroughJourneyAsync(host, browser, Oidc.AuthorizeUrl(challenge), "alice", IdentityTestHost.AlicePassword);

        var journey = Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, extra: "&prompt=login")));
        var id = Oidc.JourneyId(journey);
        var state = await host.JourneysAsync(j => j.GetAsync(id, browser.JourneyBinding(id), TestContext.Current.CancellationToken));

        state!.ReturnUrl.Should().NotContain("prompt");
    }

    [Fact]
    public async Task Discovery_advertises_endpoints_under_the_issuer_when_called_on_an_internal_host()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();

        // A relying party in a container network fetches discovery via http://middt-identity:8080/ (backchannel);
        // the browser must still be redirected to the public issuer.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        request.Headers.Host = "middt-identity:8080";
        using var response = await browser.Http.SendAsync(request, TestContext.Current.CancellationToken);
        var discovery = await Oidc.JsonAsync(response);

        discovery["issuer"]!.GetValue<string>().Should().Be("http://localhost/");
        foreach (var endpoint in new[] { "authorization_endpoint", "token_endpoint", "userinfo_endpoint", "end_session_endpoint", "revocation_endpoint", "jwks_uri" })
        {
            discovery[endpoint]!.GetValue<string>().Should().StartWith("http://localhost/", endpoint);
        }
    }
}
