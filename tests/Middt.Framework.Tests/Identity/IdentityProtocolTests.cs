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

    [Fact]
    public async Task Identity_and_oauth2_entities_are_persisted_exclusively_in_workflow_tables()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();
        var callback = await SignInThroughJourneyAsync(host, browser, Oidc.AuthorizeUrl(challenge), "alice", IdentityTestHost.AlicePassword);
        (await RedeemAsync(browser, Oidc.Query(callback)["code"], verifier)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (tables, instanceColumns, dataCount, workflowKeys) = await host.WorkflowDbAsync(async db =>
        {
            var ct = TestContext.Current.CancellationToken;
            var tableNames = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQuery<string>(
                    db.Database, $"SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"),
                ct);
            var columns = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQuery<string>(
                    db.Database, $"SELECT name AS \"Value\" FROM pragma_table_info('WorkflowInstances') ORDER BY name"),
                ct);
            var instanceDataCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                db.InstanceData, ct);
            var keys = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.Instances.Select(i => i.WorkflowKey).Distinct(), ct);
            return (tableNames, columns, instanceDataCount, keys);
        });

        tables.Should().BeEquivalentTo(["WorkflowHistory", "WorkflowIncidents", "WorkflowInstanceData", "WorkflowInstances", "WorkflowJobs"]);
        instanceColumns.Should().NotContain(["Data", "DataVersion"]);
        dataCount.Should().BeGreaterThan(0);
        workflowKeys.Should().Contain(["idp-user", "idp-role", "oauth-application", "oauth-scope", "oauth-authorization", "oauth-token"]);
    }

    [Fact]
    public async Task Pushed_authorization_request_par_flow_end_to_end()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (verifier, challenge) = Oidc.NewPkce();

        var parResponse = await browser.PostFormAsync("/connect/par", new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["redirect_uri"] = IdentityTestHost.RedirectUri,
            ["scope"] = "openid profile email api",
            ["state"] = "par-state-1",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        }, Basic("web", IdentityTestHost.WebSecret));

        parResponse.StatusCode.Should().BeOneOf([HttpStatusCode.Created, HttpStatusCode.OK],
            await parResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var parJson = await Oidc.JsonAsync(parResponse);
        var requestUri = parJson["request_uri"]!.GetValue<string>();
        requestUri.Should().NotBeNullOrWhiteSpace();

        var authorizeUrl = $"/connect/authorize?client_id=web&request_uri={Uri.EscapeDataString(requestUri)}";
        var callback = await SignInThroughJourneyAsync(host, browser, authorizeUrl, "alice", IdentityTestHost.AlicePassword);
        var query = Oidc.Query(callback);
        query["state"].Should().Be("par-state-1");

        var tokenResponse = await RedeemAsync(browser, query["code"], verifier);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await Oidc.JsonAsync(tokenResponse);
        var access = Oidc.Payload(tokens["access_token"]!.GetValue<string>());
        access["email"]!.GetValue<string>().Should().Be("alice@test.com");
    }

    [Fact]
    public async Task Device_authorization_flow_polls_pending_then_succeeds_after_user_verification()
    {
        await using var host = await IdentityTestHost.StartAsync(o =>
        {
            o.Seed.Clients.Add(new MiddtIdentitySeedClient
            {
                ClientId = "tv-device",
                Type = "public",
                DisplayName = "Akıllı TV",
                GrantTypes = [OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.DeviceCode, OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.RefreshToken],
                Scopes = ["openid", "profile", "email", "roles", "api"],
            });
        });
        using var device = host.NewBrowser();
        using var userBrowser = host.NewBrowser();

        // 1. Device requests a device_code and user_code
        var deviceResponse = await device.PostFormAsync("/connect/device", new Dictionary<string, string>
        {
            ["client_id"] = "tv-device",
            ["scope"] = "openid profile email roles api",
        });
        deviceResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deviceResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var deviceJson = await Oidc.JsonAsync(deviceResponse);
        var deviceCode = deviceJson["device_code"]!.GetValue<string>();
        var userCode = deviceJson["user_code"]!.GetValue<string>();
        deviceJson["verification_uri"]!.GetValue<string>().Should().Be("http://localhost/connect/verify");

        // 2. Polling before user approval returns authorization_pending
        var pendingResponse = await device.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.DeviceCode,
            ["client_id"] = "tv-device",
            ["device_code"] = deviceCode,
        });
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Oidc.JsonAsync(pendingResponse))["error"]!.GetValue<string>().Should().Be("authorization_pending");

        // 3. User opens verification_uri_complete -> redirected to login journey -> signs in -> redirected to /account/device?user_code=...
        var verifyUrl = $"/connect/verify?user_code={Uri.EscapeDataString(userCode)}";
        var loginRedirect = Oidc.Location(await userBrowser.GetAsync(verifyUrl));
        var journeyId = Oidc.JourneyId(loginRedirect);
        var binding = userBrowser.JourneyBinding(journeyId);
        var state = await host.JourneysAsync(j => j.SubmitAsync(journeyId, binding, "submit-credentials",
            Oidc.Input(new { userName = "alice", password = IdentityTestHost.AlicePassword }), TestContext.Current.CancellationToken));
        state!.IsSucceeded.Should().BeTrue();

        var backToVerify = Oidc.Location(await userBrowser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(journeyId)));
        backToVerify.AbsolutePath.Should().Be("/connect/verify");

        var toDevicePage = Oidc.Location(await userBrowser.GetAsync(backToVerify.PathAndQuery));
        toDevicePage.AbsolutePath.Should().Be("/account/device");
        Oidc.Query(toDevicePage)["user_code"].Should().Be(userCode);

        // 4. User approves the device request at POST /connect/verify
        var approveResponse = await userBrowser.PostFormAsync("/connect/verify", new Dictionary<string, string>
        {
            ["user_code"] = userCode,
            ["decision"] = "approve",
        });
        var approvedRedirect = Oidc.Location(approveResponse);
        approvedRedirect.PathAndQuery.Should().Be("/account/device?status=approved");

        // 5. Device polls /connect/token again and receives the access token
        var tokenResponse = await device.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.DeviceCode,
            ["client_id"] = "tv-device",
            ["device_code"] = deviceCode,
        });
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tokenResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tokens = await Oidc.JsonAsync(tokenResponse);
        var access = Oidc.Payload(tokens["access_token"]!.GetValue<string>());
        access["preferred_username"]!.GetValue<string>().Should().Be("alice");
        access["email"]!.GetValue<string>().Should().Be("alice@test.com");
    }

    [Fact]
    public async Task Password_and_token_exchange_flows_issue_tokens_with_expected_claims()
    {
        await using var host = await IdentityTestHost.StartAsync(o =>
        {
            o.Seed.Clients.Add(new MiddtIdentitySeedClient
            {
                ClientId = "legacy-cli",
                ClientSecret = "cli-secret",
                DisplayName = "CLI İstemcisi",
                GrantTypes =
                [
                    OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.Password,
                    OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.TokenExchange,
                ],
                Scopes = ["openid", "profile", "email", "roles", "api"],
            });
        });
        using var browser = host.NewBrowser();

        // 1. Password grant (valid credentials)
        var passwordResponse = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "alice",
            ["password"] = IdentityTestHost.AlicePassword,
            ["scope"] = "openid profile email roles api",
        }, Basic("legacy-cli", "cli-secret"));
        passwordResponse.StatusCode.Should().Be(HttpStatusCode.OK, await passwordResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var passwordTokens = await Oidc.JsonAsync(passwordResponse);
        var subjectToken = passwordTokens["access_token"]!.GetValue<string>();
        var access = Oidc.Payload(subjectToken);
        access["preferred_username"]!.GetValue<string>().Should().Be("alice");

        // 2. Password grant (invalid password -> invalid_grant)
        var wrongPassword = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "alice",
            ["password"] = "wrong-password",
            ["scope"] = "openid api",
        }, Basic("legacy-cli", "cli-secret"));
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Oidc.JsonAsync(wrongPassword))["error"]!.GetValue<string>().Should().Be("invalid_grant");

        // 3. Token exchange (RFC 8693)
        var exchangeResponse = await browser.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddict.Abstractions.OpenIddictConstants.GrantTypes.TokenExchange,
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = OpenIddict.Abstractions.OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
            ["scope"] = "openid profile api",
        }, Basic("legacy-cli", "cli-secret"));
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exchangeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var exchangedTokens = await Oidc.JsonAsync(exchangeResponse);
        var exchangedPayload = Oidc.Payload(exchangedTokens["access_token"]!.GetValue<string>());
        exchangedPayload["sub"]!.GetValue<string>().Should().Be(access["sub"]!.GetValue<string>());
        exchangedPayload["act"]!["sub"]!.GetValue<string>().Should().Be("legacy-cli");
    }

    [Fact]
    public async Task External_logins_authentication_tokens_and_role_claims_are_persisted_in_workflow_stores()
    {
        await using var host = await IdentityTestHost.StartAsync();

        await host.UsersAsync(async users =>
        {
            var alice = (await users.FindByNameAsync("alice"))!;

            // External logins (IUserLoginStore)
            var addLogin = await users.AddLoginAsync(alice, new Microsoft.AspNetCore.Identity.UserLoginInfo("GitHub", "gh-12345", "GitHub"));
            addLogin.Succeeded.Should().BeTrue();

            var byLogin = await users.FindByLoginAsync("GitHub", "gh-12345");
            byLogin.Should().NotBeNull();
            byLogin!.Id.Should().Be(alice.Id);

            var logins = await users.GetLoginsAsync(alice);
            logins.Should().ContainSingle(l => l.LoginProvider == "GitHub" && l.ProviderKey == "gh-12345");

            (await users.RemoveLoginAsync(alice, "GitHub", "gh-12345")).Succeeded.Should().BeTrue();
            (await users.FindByLoginAsync("GitHub", "gh-12345")).Should().BeNull();

            // Authentication tokens (IUserAuthenticationTokenStore) - encrypted at rest
            const string rawTokenSecret = "super-secret-external-token-value";
            (await users.SetAuthenticationTokenAsync(alice, "GitHub", "access_token", rawTokenSecret)).Succeeded.Should().BeTrue();
            (await users.GetAuthenticationTokenAsync(alice, "GitHub", "access_token")).Should().Be(rawTokenSecret);

            var dump = await host.DumpJourneyTablesAsync();
            dump.Should().NotContain(rawTokenSecret, "authentication tokens must be protected with DataProtection at rest");

            (await users.RemoveAuthenticationTokenAsync(alice, "GitHub", "access_token")).Succeeded.Should().BeTrue();
            (await users.GetAuthenticationTokenAsync(alice, "GitHub", "access_token")).Should().BeNull();
            return true;
        });

        await host.WithServiceAsync<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>, bool>(async roles =>
        {
            var managerRole = (await roles.FindByNameAsync("manager"))!;
            var claim = new System.Security.Claims.Claim("permission", "reports.export");

            (await roles.AddClaimAsync(managerRole, claim)).Succeeded.Should().BeTrue();
            var claims = await roles.GetClaimsAsync(managerRole);
            claims.Should().ContainSingle(c => c.Type == "permission" && c.Value == "reports.export");

            (await roles.RemoveClaimAsync(managerRole, claim)).Succeeded.Should().BeTrue();
            (await roles.GetClaimsAsync(managerRole)).Should().BeEmpty();
            return true;
        });
    }
}
