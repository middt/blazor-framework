using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Identity;
using Middt.Identity.Journeys;

namespace Middt.Framework.Tests.Identity;

/// <summary>Authentication journeys: lockout, TOTP, consent, registration, password reset, browser binding, secrets.</summary>
public sealed partial class AuthJourneyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Guid Id, string Binding)> StartLoginAsync(Browser browser, string clientId = "web", string scope = "openid profile email roles offline_access api")
    {
        var (_, challenge) = Oidc.NewPkce();
        var id = Oidc.JourneyId(Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, clientId, scope))));
        return (id, browser.JourneyBinding(id));
    }

    private static Task<AuthJourneyState?> SubmitAsync(IdentityTestHost host, Guid id, string binding, string action, object? input = null) =>
        host.JourneysAsync(j => j.SubmitAsync(id, binding, action, input is null ? null : Oidc.Input(input), Ct));

    private static async Task<(Guid Id, string Binding)> StartAsync(IdentityTestHost host, Browser browser, string journey, AuthJourneyStart start)
    {
        var handle = await host.JourneysAsync(j => j.StartAsync(journey, start, Ct));
        browser.SetJourneyCookie(handle);
        return (handle.Id, handle.Binding);
    }

    private static string LatestCode(IdentityTestHost host, string email) =>
        CodePattern().Match(host.Mailbox.Messages.First(m => m.To == email).Text).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    // ───────────── credentials & lockout ─────────────

    [Fact]
    public async Task Wrong_password_keeps_the_step_counts_for_lockout_and_fails_the_journey_after_the_limit()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var state = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "bob", password = "wrong" + attempt });
            state!.State.Should().Be("credentials");
            state.View.Should().Be("credentials");
            state.IsFinished.Should().BeFalse();
            state.Errors.Should().Equal("Kullanıcı adı veya şifre hatalı.");
            state.Data["failedAttempts"]!.GetValue<int>().Should().Be(attempt);
        }

        (await host.UsersAsync(async u => (await u.FindByNameAsync("bob"))!.AccessFailedCount)).Should().Be(4);

        var last = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "bob", password = "wrong5" });
        last!.State.Should().Be("failed");
        last.IsFinished.Should().BeTrue();
        last.IsSucceeded.Should().BeFalse();
        (await host.UsersAsync(async u => await u.IsLockedOutAsync((await u.FindByNameAsync("bob"))!))).Should().BeTrue();

        // a new journey with the right password is refused while the account is locked
        using var other = host.NewBrowser();
        var (id2, binding2) = await StartLoginAsync(other);
        var locked = await SubmitAsync(host, id2, binding2, "submit-credentials", new { userName = "bob", password = IdentityTestHost.BobPassword });
        locked!.State.Should().Be("credentials");
        locked.Errors.Should().ContainSingle().Which.Should().Contain("kilitlendi");
        (await host.JourneysAsync(j => j.CompleteAsync(id2, binding2, Ct))).Should().BeNull();
    }

    [Fact]
    public async Task Unknown_user_gets_the_same_answer_as_a_wrong_password()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);

        var state = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "nobody", password = "x" });

        state!.Errors.Should().Equal("Kullanıcı adı veya şifre hatalı.");
        state.State.Should().Be("credentials");
    }

    [Fact]
    public async Task Missing_input_is_reported_with_turkish_field_titles()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);

        var state = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice" });
        state!.Errors.Should().Equal("Şifre: Bu alan zorunludur.");
        state.State.Should().Be("credentials");

        var unknown = await SubmitAsync(host, id, binding, "approve");
        unknown!.Errors.Should().Equal("Bu işlem şu anda kullanılamaz.");
    }

    [Fact]
    public async Task Login_journey_offers_registration_and_password_reset_as_redirect_hints()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);

        var state = await SubmitAsync(host, id, binding, "forgot-password");

        state!.IsFinished.Should().BeTrue();
        state.IsSucceeded.Should().BeFalse();
        state.Data["redirect"]!.GetValue<string>().Should().Be("/account/forgot-password");
        state.ReturnUrl.Should().StartWith("/connect/authorize");
    }

    // ───────────── TOTP ─────────────

    [Fact]
    public async Task Totp_enrollment_then_login_requires_the_authenticator_code()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var aliceId = await host.UsersAsync(async u => (await u.FindByNameAsync("alice"))!.Id);

        // enrollment (signed-in user)
        var (enrollId, enrollBinding) = await StartAsync(host, browser, AuthJourneys.EnrollTotp, new AuthJourneyStart { UserId = aliceId, ReturnUrl = "/account/security" });
        var setup = await host.JourneysAsync(j => j.GetAsync(enrollId, enrollBinding, Ct));
        setup!.View.Should().Be("totp-setup");
        var uri = setup.Data["totpUri"]!.GetValue<string>();
        uri.Should().StartWith("otpauth://totp/Middt:alice%40test.com?secret=");
        var key = Regex.Match(uri, "secret=([A-Z2-7]+)").Groups[1].Value;
        setup.Data["totpKey"]!.GetValue<string>().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant().Should().Be(key);

        var wrong = await SubmitAsync(host, enrollId, enrollBinding, "confirm", new { code = "000000" == Totp.Compute(key, DateTimeOffset.UtcNow) ? "111111" : "000000" });
        wrong!.State.Should().Be("setup");
        wrong.Errors.Should().Equal("Doğrulama kodu hatalı.");

        var enabled = await SubmitAsync(host, enrollId, enrollBinding, "confirm", new { code = Totp.Compute(key, DateTimeOffset.UtcNow) });
        enabled!.State.Should().Be("enabled");
        enabled.IsSucceeded.Should().BeTrue();
        enabled.Data.ContainsKey("totpKey").Should().BeFalse("the key is only shown during the setup step");
        (await host.UsersAsync(async u => (await u.FindByIdAsync(aliceId))!.TwoFactorEnabled)).Should().BeTrue();
        Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(enrollId))).AbsolutePath.Should().Be("/account/security");

        // login: credentials → mfa → code
        using var other = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(other);
        var mfa = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword });
        mfa!.State.Should().Be("mfa");
        mfa.View.Should().Be("totp");
        (await host.JourneysAsync(j => j.CompleteAsync(id, binding, Ct))).Should().BeNull("credentials alone do not finish the journey");

        var bad = await SubmitAsync(host, id, binding, "submit-totp", new { code = "12345678" });
        bad!.State.Should().Be("mfa");
        bad.Errors.Should().Equal("Doğrulama kodu hatalı.");

        var done = await SubmitAsync(host, id, binding, "submit-totp", new { code = Totp.Compute(key, DateTimeOffset.UtcNow) });
        done!.State.Should().Be("authenticated");
        var callback = Oidc.Location(await other.GetAsync(Oidc.Location(await other.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id))).PathAndQuery));
        Oidc.Query(callback).Should().ContainKey("code");

        (await host.DumpJourneyTablesAsync()).Should().NotContain(key, "the pending key is stored encrypted only");
    }

    // ───────────── consent ─────────────

    [Fact]
    public async Task Explicit_consent_client_asks_once_and_remembers_the_grant()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser, "consent-app", "openid profile api");

        var consent = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword });
        consent!.State.Should().Be("consent");
        consent.View.Should().Be("consent");
        consent.Data["clientName"]!.GetValue<string>().Should().Be("Üçüncü taraf uygulama");
        consent.Data["scopeDetails"]!.AsArray().Select(s => s!["displayName"]!.GetValue<string>())
            .Should().Equal("Kimlik bilginiz", "Profil bilgileriniz (ad, kullanıcı adı)", "Demo API");
        consent.Actions.Select(a => a.Key).Should().BeEquivalentTo("approve", "deny");

        var approved = await SubmitAsync(host, id, binding, "approve");
        approved!.State.Should().Be("authenticated");
        var back = Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id)));
        Oidc.Query(Oidc.Location(await browser.GetAsync(back.PathAndQuery))).Should().ContainKey("code");

        // remembered: the next request is answered without a journey
        var (_, challenge) = Oidc.NewPkce();
        var direct = Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, "consent-app", "openid profile api")));
        Oidc.Query(direct).Should().ContainKey("code");

        // prompt=consent asks again (consent-only journey for the signed-in user)
        var again = Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, "consent-app", "openid profile api", extra: "&prompt=consent")));
        var consentId = Oidc.JourneyId(again);
        var consentOnly = await host.JourneysAsync(j => j.GetAsync(consentId, browser.JourneyBinding(consentId), Ct));
        consentOnly!.State.Should().Be("consent");
        consentOnly.ReturnUrl.Should().NotContain("prompt");
    }

    [Fact]
    public async Task Denied_consent_returns_access_denied_to_the_client()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser, "consent-app", "openid profile");
        await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "bob", password = IdentityTestHost.BobPassword });

        var denied = await SubmitAsync(host, id, binding, "deny");
        denied!.State.Should().Be("denied");
        denied.IsSucceeded.Should().BeTrue("the journey ended normally; the client receives the denial");

        var back = Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id)));
        back.Query.Should().Contain(MiddtIdentityEndpoints.ConsentDeniedParameter);
        var callback = Oidc.Location(await browser.GetAsync(back.PathAndQuery));
        callback.GetLeftPart(UriPartial.Path).Should().Be(IdentityTestHost.RedirectUri);
        Oidc.Query(callback)["error"].Should().Be("access_denied");
        Oidc.Query(callback)["state"].Should().Be("st-123");

        // nothing was granted: the next request asks again
        var (_, challenge) = Oidc.NewPkce();
        Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge, "consent-app", "openid profile"))).AbsolutePath.Should().StartWith("/account/journey/");
    }

    // ───────────── registration ─────────────

    [Fact]
    public async Task Registration_with_email_code_from_the_development_mailbox_signs_in()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartAsync(host, browser, AuthJourneys.Register, new AuthJourneyStart { ReturnUrl = "/welcome" });

        var weak = await SubmitAsync(host, id, binding, "submit-details", new { userName = "carol", email = "carol@test.com", displayName = "Carol", password = "short" });
        weak!.State.Should().Be("details");
        weak.Errors.Should().ContainSingle().Which.Should().Contain("Şifre en az 6 karakter olmalıdır.");

        var taken = await SubmitAsync(host, id, binding, "submit-details", new { userName = "alice", email = "carol@test.com", password = "Carol-Pass1!" });
        taken!.Errors.Should().ContainSingle().Which.Should().Contain("Bu kullanıcı adı zaten kullanılıyor.");

        var verify = await SubmitAsync(host, id, binding, "submit-details", new { userName = "carol", email = "carol@test.com", displayName = "Carol", password = "Carol-Pass1!" });
        verify!.State.Should().Be("verify-email");
        verify.View.Should().Be("email-code");
        verify.Data["codeSentTo"]!.GetValue<string>().Should().Be("carol@test.com");
        (await host.UsersAsync(async u => (await u.FindByNameAsync("carol"))!.EmailConfirmed)).Should().BeFalse();

        var code = LatestCode(host, "carol@test.com");
        var wrong = await SubmitAsync(host, id, binding, "submit-code", new { code = code == "000000" ? "111111" : "000000" });
        wrong!.State.Should().Be("verify-email");
        wrong.Errors.Should().Equal("Doğrulama kodu hatalı veya süresi dolmuş.");

        var resent = await SubmitAsync(host, id, binding, "resend-code");
        resent!.State.Should().Be("verify-email");
        var newCode = LatestCode(host, "carol@test.com");
        host.Mailbox.Messages.Count(m => m.To == "carol@test.com").Should().Be(2);

        var registered = await SubmitAsync(host, id, binding, "submit-code", new { code = newCode });
        registered!.State.Should().Be("registered");
        registered.IsSucceeded.Should().BeTrue();
        (await host.UsersAsync(async u => (await u.FindByNameAsync("carol"))!.EmailConfirmed)).Should().BeTrue();

        Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id))).AbsolutePath.Should().Be("/welcome");
        browser.HasSessionCookie.Should().BeTrue();
        var (_, challenge) = Oidc.NewPkce();
        Oidc.Query(Oidc.Location(await browser.GetAsync(Oidc.AuthorizeUrl(challenge)))).Should().ContainKey("code");

        var tables = await host.DumpJourneyTablesAsync();
        tables.Should().NotContain("Carol-Pass1!").And.NotContain(newCode).And.NotContain(binding);
    }

    [Fact]
    public async Task Registration_can_be_disabled()
    {
        await using var host = await IdentityTestHost.StartAsync(o => o.AllowRegistration = false);
        using var browser = host.NewBrowser();

        var (id, binding) = await StartAsync(host, browser, AuthJourneys.Register, new AuthJourneyStart());
        var state = await host.JourneysAsync(j => j.GetAsync(id, binding, Ct));

        state!.State.Should().Be("disabled");
        state.IsFinished.Should().BeTrue();
        state.IsSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Registration_without_email_confirmation_finishes_directly()
    {
        await using var host = await IdentityTestHost.StartAsync(o => o.RequireConfirmedEmail = false);
        using var browser = host.NewBrowser();
        var (id, binding) = await StartAsync(host, browser, AuthJourneys.Register, new AuthJourneyStart());

        var state = await SubmitAsync(host, id, binding, "submit-details", new { userName = "dave", email = "dave@test.com", password = "Dave-Pass1!" });

        state!.State.Should().Be("registered");
        host.Mailbox.Messages.Should().BeEmpty();
    }

    // ───────────── password reset ─────────────

    [Fact]
    public async Task Password_reset_does_not_reveal_whether_an_email_exists()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();

        var (unknownId, unknownBinding) = await StartAsync(host, browser, AuthJourneys.ResetPassword, new AuthJourneyStart());
        var unknown = await SubmitAsync(host, unknownId, unknownBinding, "submit-email", new { email = "ghost@test.com" });
        var (knownId, knownBinding) = await StartAsync(host, browser, AuthJourneys.ResetPassword, new AuthJourneyStart());
        var known = await SubmitAsync(host, knownId, knownBinding, "submit-email", new { email = "bob@test.com" });

        unknown!.State.Should().Be(known!.State).And.Be("verify");
        unknown.View.Should().Be(known.View);
        unknown.Data.Select(p => p.Key).Order(StringComparer.Ordinal).Should().Equal(known.Data.Select(p => p.Key).Order(StringComparer.Ordinal));
        host.Mailbox.Messages.Should().ContainSingle().Which.To.Should().Be("bob@test.com");

        var guess = await SubmitAsync(host, unknownId, unknownBinding, "submit-code", new { code = "123456" });
        guess!.State.Should().Be("verify");
        guess.Errors.Should().Equal("Doğrulama kodu hatalı veya süresi dolmuş.");
    }

    [Fact]
    public async Task Password_reset_with_the_emailed_code_sets_a_new_password_and_unlocks()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        await host.UsersAsync(async u =>
        {
            var bob = (await u.FindByNameAsync("bob"))!;
            return await u.SetLockoutEndDateAsync(bob, DateTimeOffset.UtcNow.AddHours(1));
        });

        var (id, binding) = await StartAsync(host, browser, AuthJourneys.ResetPassword, new AuthJourneyStart { ReturnUrl = "/account/login" });
        await SubmitAsync(host, id, binding, "submit-email", new { email = "bob@test.com" });
        var newPassword = await SubmitAsync(host, id, binding, "submit-code", new { code = LatestCode(host, "bob@test.com") });
        newPassword!.State.Should().Be("new-password");

        var weak = await SubmitAsync(host, id, binding, "submit-password", new { password = "abc" });
        weak!.State.Should().Be("new-password");
        weak.Errors.Should().ContainSingle().Which.Should().Contain("Şifre en az 6 karakter olmalıdır.");

        var done = await SubmitAsync(host, id, binding, "submit-password", new { password = "Bob-New-Pass2!" });
        done!.State.Should().Be("done");
        Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id))).AbsolutePath.Should().Be("/account/login");
        browser.HasSessionCookie.Should().BeFalse("a password reset does not sign in");

        using var other = host.NewBrowser();
        var (loginId, loginBinding) = await StartLoginAsync(other);
        (await SubmitAsync(host, loginId, loginBinding, "submit-credentials", new { userName = "bob@test.com", password = "Bob-New-Pass2!" }))!
            .State.Should().Be("authenticated", "the account was unlocked and the e-mail can be used as user name");
        (await host.DumpJourneyTablesAsync()).Should().NotContain("Bob-New-Pass2!");
    }

    // ───────────── binding, completion, expiry ─────────────

    [Fact]
    public async Task Another_browser_cannot_read_submit_or_complete_a_journey_and_completion_works_once()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        using var attacker = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);
        var forged = JourneySecrets.NewSecret();

        (await host.JourneysAsync(j => j.GetAsync(id, forged, Ct))).Should().BeNull();
        (await host.JourneysAsync(j => j.GetAsync(id, string.Empty, Ct))).Should().BeNull();
        (await SubmitAsync(host, id, forged, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword })).Should().BeNull();
        (await host.JourneysAsync(j => j.GetAsync(Guid.NewGuid(), binding, Ct))).Should().BeNull();

        await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword });
        (await host.JourneysAsync(j => j.CompleteAsync(id, forged, Ct))).Should().BeNull();

        // the attacker has no journey cookie: no session
        var stolen = await attacker.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id));
        Oidc.Location(stolen).AbsolutePath.Should().Be(MiddtIdentityDefaults.LoginPath);
        attacker.HasSessionCookie.Should().BeFalse();

        // the owner completes exactly once (concurrent completions: one winner)
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.JourneysAsync(j => j.CompleteAsync(id, binding, Ct))));
        results.Count(r => r is not null).Should().Be(1);
        results.Single(r => r is not null)!.SignIn.Should().BeTrue();
        (await host.JourneysAsync(j => j.GetAsync(id, binding, Ct))).Should().BeNull("a consumed journey is gone");
        var again = await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id));
        again.StatusCode.Should().Be(HttpStatusCode.Redirect);
        browser.HasSessionCookie.Should().BeFalse();
    }

    [Fact]
    public async Task Journey_data_never_exposes_internal_values_and_return_urls_must_be_local()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartAsync(host, browser, AuthJourneys.Login, new AuthJourneyStart
        {
            ReturnUrl = "//evil.example/steal",
            Data = new System.Text.Json.Nodes.JsonObject { ["authenticated"] = true, ["_bindingHash"] = "x", ["hint"] = "alice" },
        });

        var state = await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = "wrong" });

        state!.ReturnUrl.Should().BeNull();
        state.Data["hint"]!.GetValue<string>().Should().Be("alice");
        state.Data.Select(p => p.Key).Should().NotContain(k => k.StartsWith('_')).And.NotContain("form").And.NotContain("error");
        state.Data["authenticated"]!.GetValue<bool>().Should().BeFalse("start data cannot set reserved flags");

        await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword });
        Oidc.Location(await browser.GetAsync(MiddtIdentityDefaults.JourneyCompletePath(id))).ToString().Should().Be("http://localhost/");
    }

    [Fact]
    public async Task Expired_journeys_are_not_available()
    {
        await using var host = await IdentityTestHost.StartAsync(o => o.JourneyLifetime = TimeSpan.Zero);
        using var browser = host.NewBrowser();
        var (id, binding) = await StartAsync(host, browser, AuthJourneys.Login, new AuthJourneyStart());

        (await host.JourneysAsync(j => j.GetAsync(id, binding, Ct))).Should().BeNull();
        (await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword })).Should().BeNull();
    }

    [Fact]
    public async Task Journeys_are_not_reachable_with_the_workflow_roles_of_ordinary_users()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, _) = await StartLoginAsync(browser);

        var act = () => host.WithServiceAsync<Middt.Framework.Workflow.Runtime.WorkflowEngine, object>(async e =>
            await e.GetAsync(id, new Middt.Framework.Workflow.Expressions.WorkflowUser { Id = "u", Name = "u", Roles = ["manager"] }, Ct));

        await act.Should().ThrowAsync<Middt.Framework.Workflow.Runtime.WorkflowForbiddenException>();
    }

    // ───────────── persistence of secrets ─────────────

    [Fact]
    public async Task Passwords_codes_and_binding_secrets_are_never_stored_in_the_journey_tables()
    {
        await using var host = await IdentityTestHost.StartAsync();
        using var browser = host.NewBrowser();
        var (id, binding) = await StartLoginAsync(browser);
        await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = "Wrong-Secret-9" });
        await SubmitAsync(host, id, binding, "submit-credentials", new { userName = "alice", password = IdentityTestHost.AlicePassword });

        var tables = await host.DumpJourneyTablesAsync();

        tables.Should().NotContain(IdentityTestHost.AlicePassword).And.NotContain("Wrong-Secret-9").And.NotContain(binding);
        tables.Should().Contain("\"userName\":\"alice\"", "the non-sensitive part of the form is kept for the audit trail");
        var history = await host.WorkflowDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == id && h.TransitionKey == "submit-credentials").ToListAsync(Ct));
        history.Should().HaveCount(2).And.AllSatisfy(h => h.Input.Should().Be("""{"userName":"alice"}"""));
    }

    // ───────────── seeding, overrides ─────────────

    [Fact]
    public async Task Seeding_is_idempotent_updates_clients_and_keeps_their_secrets()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        await using (connection)
        {
            await using (var first = await IdentityTestHost.StartAsync(connection: connection))
            {
            }

            await using var second = await IdentityTestHost.StartAsync(o =>
            {
                o.Seed.Clients.Single(c => c.ClientId == "web").RedirectUris.Add("http://localhost/second-callback");
                o.Seed.Clients.Single(c => c.ClientId == "service").ClientSecret = "ignored-new-secret";
            }, connection);
            using var browser = second.NewBrowser();

            var token = await browser.PostFormAsync("/connect/token", new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "api" },
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("service:" + IdentityTestHost.ServiceSecret))));
            token.StatusCode.Should().Be(HttpStatusCode.OK, "an existing secret is not replaced by the seed");

            var (_, challenge) = Oidc.NewPkce();
            var journey = await browser.GetAsync(Oidc.AuthorizeUrl(challenge, redirectUri: "http://localhost/second-callback"));
            Oidc.Location(journey).AbsolutePath.Should().StartWith("/account/journey/", "the added redirect URI was applied");
            (await second.UsersAsync(u => u.Users.CountAsync(Ct))).Should().Be(2);
        }
    }

    [Fact]
    public async Task A_journey_directory_overrides_a_default_journey_by_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), "middt-journeys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "reset.json"), """
                {
                  "key": "auth-reset-password", "version": 1, "roles": [ "idp-journey" ],
                  "start": { "key": "start", "target": "closed",
                             "input": { "schema": { "type": "object", "properties": { "_bindingHash": { "type": "string" } } } } },
                  "states": [ { "key": "closed", "title": "Kapalı", "type": "Finish", "subType": "Error" } ]
                }
                """, Ct);
            await using var host = await IdentityTestHost.StartAsync(o => o.AddJourneysFromDirectory(directory));
            using var browser = host.NewBrowser();

            var (id, binding) = await StartAsync(host, browser, AuthJourneys.ResetPassword, new AuthJourneyStart());

            (await host.JourneysAsync(j => j.GetAsync(id, binding, Ct)))!.StateTitle.Should().Be("Kapalı");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Login_journey_shows_the_client_name_on_the_first_step()
    {
        await using var host = await IdentityTestHost.StartAsync();
        var handle = await host.JourneysAsync(j => j.StartAsync(AuthJourneys.Login, new AuthJourneyStart { ClientId = "web", Scopes = ["openid"] }, TestContext.Current.CancellationToken));
        var state = await host.JourneysAsync(j => j.GetAsync(handle.Id, handle.Binding, TestContext.Current.CancellationToken));

        state!.View.Should().Be("credentials");
        state.Data["clientName"]!.GetValue<string>().Should().Be("Web uygulaması");
    }

    [Fact]
    public async Task Signed_in_users_without_permission_are_sent_to_the_access_denied_page()
    {
        await using var host = await IdentityTestHost.StartAsync();
        var cookie = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(MiddtIdentityDefaults.CookieScheme);

        cookie.AccessDeniedPath.Value.Should().Be(MiddtIdentityDefaults.AccessDeniedPath);
        cookie.LoginPath.Value.Should().Be(MiddtIdentityDefaults.LoginPath);
    }
}
