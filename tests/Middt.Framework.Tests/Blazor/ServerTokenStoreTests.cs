using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Configuration;
using Middt.Framework.Tests.Infrastructure;
using Middt.Framework.Tests.Security;
using Middt.Framework.Bff.Security;

namespace Middt.Framework.Tests.Blazor;

public sealed class ServerTokenStoreTests
{
    private const string AccessToken = "eyJhbGciOiJIUzI1NiJ9.access-token-payload.signature";
    private const string RefreshToken = "refresh-token-value-0123456789";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly TestDistributedCache cache;
    private readonly DistributedServerTokenStore store;

    public ServerTokenStoreTests()
    {
        cache = new TestDistributedCache(time);
        store = new DistributedServerTokenStore(
            cache,
            new EphemeralDataProtectionProvider(),
            Options.Create(new GeneralSettings { SessionTimeoutInMinute = 10, SessionAbsoluteTimeoutInMinute = 60 }),
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveTokenResponseModel Tokens(DateTimeOffset? expires = null) =>
        new() { access_token = AccessToken, refresh_token = RefreshToken, expires_in = 600, expire_datetime = expires };

    [Fact]
    public async Task Entry_that_cannot_be_decrypted_is_treated_as_signed_out()
    {
        // Simulates lost Data Protection keys while the shared cache (e.g. Redis) kept the session entry.
        await store.SetAsync("s1", Tokens(), Ct);
        var storeWithOtherKeys = new DistributedServerTokenStore(
            cache,
            new EphemeralDataProtectionProvider(),
            Options.Create(new GeneralSettings { SessionTimeoutInMinute = 10, SessionAbsoluteTimeoutInMinute = 60 }),
            time);

        (await storeWithOtherKeys.GetAsync("s1", touch: true, Ct)).Should().BeNull();
        cache.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Stored_tokens_are_returned()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        var tokens = await store.GetAsync("s1", touch: false, Ct);

        tokens!.access_token.Should().Be(AccessToken);
        tokens.refresh_token.Should().Be(RefreshToken);
    }

    [Fact]
    public async Task Unknown_session_returns_null()
    {
        (await store.GetAsync("nope", touch: true, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Cache_entries_are_encrypted()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        var raw = cache.RawValues.Should().ContainSingle().Subject;
        var text = Encoding.UTF8.GetString(raw);
        text.Should().NotContain(AccessToken).And.NotContain("access-token-payload").And.NotContain(RefreshToken);
        cache.Keys.Single().Should().EndWith("s1");
    }

    [Fact]
    public async Task Session_ends_after_the_idle_timeout()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        (await store.GetAsync("s1", touch: true, Ct)).Should().BeNull();
        cache.Keys.Should().BeEmpty("timed out sessions are removed");
    }

    [Fact]
    public async Task Activity_extends_the_idle_timeout()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        for (var i = 0; i < 4; i++)
        {
            time.Advance(TimeSpan.FromMinutes(9));
            (await store.GetAsync("s1", touch: true, Ct)).Should().NotBeNull($"activity after {(i + 1) * 9} minutes keeps the session alive");
        }
    }

    [Fact]
    public async Task Get_without_touch_does_not_extend_the_idle_timeout()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        time.Advance(TimeSpan.FromMinutes(9));
        (await store.GetAsync("s1", touch: false, Ct)).Should().NotBeNull();

        time.Advance(TimeSpan.FromMinutes(2));
        (await store.GetAsync("s1", touch: false, Ct)).Should().BeNull("background revalidation must not keep an idle session alive");
    }

    [Fact]
    public async Task Absolute_expiry_ends_the_session_despite_activity()
    {
        var start = time.GetUtcNow();
        await store.SetAsync("s1", Tokens(start.AddMinutes(15)), Ct);

        time.Advance(TimeSpan.FromMinutes(7));
        (await store.GetAsync("s1", touch: true, Ct)).Should().NotBeNull();
        time.Advance(TimeSpan.FromMinutes(7));
        (await store.GetAsync("s1", touch: true, Ct)).Should().NotBeNull();

        time.Advance(TimeSpan.FromMinutes(1));
        (await store.GetAsync("s1", touch: true, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_keeps_a_provided_absolute_expiry()
    {
        var expires = time.GetUtcNow().AddMinutes(5);

        await store.SetAsync("s1", Tokens(expires), Ct);

        (await store.GetAsync("s1", touch: false, Ct))!.expire_datetime.Should().Be(expires);
    }

    [Fact]
    public async Task SetAsync_without_expiry_uses_the_configured_absolute_timeout()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        (await store.GetAsync("s1", touch: false, Ct))!.expire_datetime.Should().Be(time.GetUtcNow().AddMinutes(60));
    }

    [Fact]
    public async Task Overwriting_tokens_resets_activity_but_not_a_kept_absolute_expiry()
    {
        var expires = time.GetUtcNow().AddMinutes(20);
        await store.SetAsync("s1", Tokens(expires), Ct);

        time.Advance(TimeSpan.FromMinutes(8));
        await store.SetAsync("s1", new SaveTokenResponseModel { access_token = "new", refresh_token = "new-r", expire_datetime = expires }, Ct);

        time.Advance(TimeSpan.FromMinutes(8));
        (await store.GetAsync("s1", touch: false, Ct))!.access_token.Should().Be("new");

        time.Advance(TimeSpan.FromMinutes(5));
        (await store.GetAsync("s1", touch: false, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_ends_the_session()
    {
        await store.SetAsync("s1", Tokens(), Ct);

        await store.RemoveAsync("s1", Ct);

        (await store.GetAsync("s1", touch: false, Ct)).Should().BeNull();
    }
}

public sealed class ServerSessionStateTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly DistributedServerTokenStore store;
    private readonly FakeNavigationManager navigation = new();

    public ServerSessionStateTests() =>
        store = new DistributedServerTokenStore(
            new TestDistributedCache(time),
            new EphemeralDataProtectionProvider(),
            Options.Create(new GeneralSettings { SessionTimeoutInMinute = 10, SessionAbsoluteTimeoutInMinute = 60 }),
            time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServerSessionState Create(string? sessionId = "s1")
    {
        var identity = sessionId is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(MiddtClaimTypes.SessionId, sessionId)], "test");
        return new ServerSessionState(new FixedAuthenticationStateProvider(new ClaimsPrincipal(identity)), store, navigation, Options.Create(new SecuritySettings()));
    }

    [Fact]
    public async Task Reads_tokens_of_the_circuit_users_session()
    {
        await store.SetAsync("s1", new SaveTokenResponseModel { access_token = "a1", refresh_token = "r1", expires_in = 600 }, Ct);
        var state = Create();

        (await state.Token()).Should().Be("a1");
        (await state.RefreshToken()).Should().Be("r1");
        (await state.ExpiresIn()).Should().Be(600);
    }

    [Fact]
    public async Task Anonymous_circuit_has_no_tokens()
    {
        await store.SetAsync("s1", new SaveTokenResponseModel { access_token = "a1" }, Ct);
        var state = Create(sessionId: null);

        (await state.Token()).Should().BeEmpty();
        (await state.RefreshToken()).Should().BeEmpty();
    }

    [Fact]
    public async Task SetToken_after_refresh_keeps_the_absolute_session_expiry()
    {
        var expires = time.GetUtcNow().AddMinutes(30);
        await store.SetAsync("s1", new SaveTokenResponseModel { access_token = "a1", refresh_token = "r1", expire_datetime = expires }, Ct);

        await Create().SetToken(new TokenResponseModel { access_token = "a2", refresh_token = "r2" });

        var stored = await store.GetAsync("s1", touch: false, Ct);
        stored!.access_token.Should().Be("a2");
        stored.refresh_token.Should().Be("r2");
        stored.expire_datetime.Should().Be(expires, "refreshing tokens must not extend the session");
    }

    [Fact]
    public async Task SetToken_does_not_resurrect_an_ended_session()
    {
        // e.g. the session timed out / was signed out elsewhere while a token refresh was in flight
        await Create().SetToken(new TokenResponseModel { access_token = "a2", refresh_token = "r2" });

        (await store.GetAsync("s1", touch: false, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ClearToken_removes_the_session_and_navigates_to_login_with_return_url()
    {
        await store.SetAsync("s1", new SaveTokenResponseModel { access_token = "a1", refresh_token = "r1" }, Ct);

        await Create().ClearToken();

        (await store.GetAsync("s1", touch: false, Ct)).Should().BeNull();
        navigation.NavigatedTo.Should().Be("/Account/Login?returnUrl=%2Forders%2F5");
        navigation.ForceLoad.Should().BeTrue();
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("http://localhost/", "http://localhost/orders/5");

        public string? NavigatedTo { get; private set; }

        public bool ForceLoad { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            NavigatedTo = uri;
            ForceLoad = options.ForceLoad;
        }
    }
}
