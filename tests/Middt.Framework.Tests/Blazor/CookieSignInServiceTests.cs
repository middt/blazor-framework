using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Tests.Client;
using Middt.Framework.Tests.Infrastructure;
using Middt.Framework.Bff.Security;

namespace Middt.Framework.Tests.Blazor;

public sealed class CookieSignInServiceTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly DistributedServerTokenStore store;
    private readonly RecordingAuthenticationService authentication = new();

    public CookieSignInServiceTests() =>
        store = new DistributedServerTokenStore(
            new TestDistributedCache(time), new EphemeralDataProtectionProvider(), Options.Create(new GeneralSettings()), time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CookieSignInService Create(FakeTokenService tokens) =>
        new(tokens, store, Options.Create(new GeneralSettings { SessionAbsoluteTimeoutInMinute = 30 }), time, NullLogger<CookieSignInService>.Instance);

    private DefaultHttpContext Context(ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
    }

    [Fact]
    public async Task Failed_login_neither_creates_a_session_nor_signs_in()
    {
        var tokens = new FakeTokenService(_ => new TokenResponseModel()) { Login = _ => new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["no"] } };

        var result = await Create(tokens).SignInAsync(Context(), "u", "p", Ct);

        result.IsSuccess.Should().BeFalse();
        authentication.SignedIn.Should().BeNull();
    }

    [Fact]
    public async Task Successful_login_stores_tokens_under_a_fresh_session_with_absolute_expiry()
    {
        var access = new Middt.Framework.Api.Security.JwtTokenGenerator(
            Options.Create(new JwtSettings { Secret = Api.TestApiHost.Secret, Issuer = "i", Audience = "a" }), TimeProvider.System)
            .CreateToken([new Claim(ClaimTypes.Sid, "7"), new Claim(ClaimTypes.Name, "alice")]).access_token;
        var tokens = new FakeTokenService(_ => new TokenResponseModel())
        {
            Login = _ => new TokenResponseModel { Result = ResultEnum.Success, access_token = access, refresh_token = "r1" },
        };

        await Create(tokens).SignInAsync(Context(), "alice", "p", Ct);

        var (principal, properties) = authentication.SignedIn!.Value;
        var sessionId = principal.FindFirst(MiddtClaimTypes.SessionId)!.Value;
        var stored = await store.GetAsync(sessionId, touch: false, Ct);
        stored!.refresh_token.Should().Be("r1");
        stored.expire_datetime.Should().Be(time.GetUtcNow().AddMinutes(30));
        properties!.ExpiresUtc.Should().Be(stored.expire_datetime);
        properties.IsPersistent.Should().BeFalse();
        properties.AllowRefresh.Should().BeFalse();
        principal.Identity!.Name.Should().Be("alice");
        principal.Claims.Select(c => c.Type).Should().NotContain(["exp", "nbf", "iat", "iss", "aud"]);
    }

    [Fact]
    public async Task Sign_out_of_an_already_ended_session_does_not_call_the_api_but_still_signs_out()
    {
        var tokens = new FakeTokenService(_ => new TokenResponseModel());
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MiddtClaimTypes.SessionId, "gone-session-id")], "cookie"));

        await Create(tokens).SignOutAsync(Context(user), Ct);

        tokens.RevokeRequests.Should().BeEmpty();
        authentication.SignedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Sign_out_revokes_and_removes_the_session()
    {
        var tokens = new FakeTokenService(_ => new TokenResponseModel());
        await store.SetAsync("session-12345", new SaveTokenResponseModel { access_token = "a1", refresh_token = "r1" }, Ct);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MiddtClaimTypes.SessionId, "session-12345")], "cookie"));

        await Create(tokens).SignOutAsync(Context(user), Ct);

        tokens.RevokeRequests.Should().Equal(("a1", "r1"));
        (await store.GetAsync("session-12345", touch: false, Ct)).Should().BeNull();
        authentication.SignedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Circuit_revalidation_fails_once_the_server_session_is_gone()
    {
        await store.SetAsync("live", new SaveTokenResponseModel { access_token = "a" }, Ct);
        var services = new ServiceCollection().AddSingleton<IServerTokenStore>(store).BuildServiceProvider();
        using var provider = new MiddtAuthenticationStateProvider(NullLoggerFactory.Instance, services.GetRequiredService<IServiceScopeFactory>());
        var validate = typeof(MiddtAuthenticationStateProvider).GetMethod("ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Task<bool> ValidateAsync(string? sessionId)
        {
            var identity = sessionId is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(MiddtClaimTypes.SessionId, sessionId)], "cookie");
            return (Task<bool>)validate.Invoke(provider, [new AuthenticationState(new ClaimsPrincipal(identity)), Ct])!;
        }

        (await ValidateAsync("live")).Should().BeTrue();
        (await ValidateAsync("ended")).Should().BeFalse();
        (await ValidateAsync(null)).Should().BeFalse();
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public (ClaimsPrincipal Principal, AuthenticationProperties? Properties)? SignedIn { get; private set; }

        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedIn = (principal, properties);
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
