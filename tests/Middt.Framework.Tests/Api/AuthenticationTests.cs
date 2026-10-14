using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Tests.Api;

public sealed class AuthenticationTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    private const string SecureUri = "api/v1/secureitems/getall";

    private TestApiHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpStatusCode> GetSecureAsync(string? token)
    {
        using var request = TestApiHost.Authorized(HttpMethod.Get, SecureUri, token);
        using var response = await Host.Client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private static JwtTokenGenerator Generator(string secret = TestApiHost.Secret, DateTimeOffset? now = null) =>
        new(
            Options.Create(new JwtSettings { Secret = secret, Issuer = TestApiHost.Issuer, Audience = TestApiHost.Audience, Expires = 60 }),
            now is null ? TimeProvider.System : new FixedTimeProvider(now.Value));

    private static Claim[] UserClaims => [new(ClaimTypes.Sid, FakeUserStore.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)), new(ClaimTypes.Name, FakeUserStore.Username)];

    [Fact]
    public async Task Secured_controller_returns_401_without_token()
    {
        (await GetSecureAsync(null)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Secured_controller_returns_200_with_token_from_login()
    {
        var token = await Host.LoginAsync();

        token.Result.Should().Be(ResultEnum.Success);
        token.access_token.Should().NotBeNullOrEmpty();
        token.refresh_token.Should().NotBeNullOrEmpty();
        token.expires_in.Should().Be(600);
        token.Username.Should().Be(FakeUserStore.Username);

        (await GetSecureAsync(token.access_token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_with_error_envelope()
    {
        using var response = await Host.Client.PostAsJsonAsync(
            "api/v1/token/login", new LoginRequestModel { username = FakeUserStore.Username, password = "wrong" }, TestApiHost.Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, Ct);
        body!.Result.Should().Be(ResultEnum.Error);
        body.access_token.Should().BeNull();
        body.MessageList.Should().Equal("Kullanıcı adı veya şifreniz hatalı.");
    }

    [Fact]
    public async Task Login_with_missing_credentials_returns_400()
    {
        using var response = await Host.Client.PostAsJsonAsync("api/v1/token/login", new LoginRequestModel { username = "alice" }, TestApiHost.Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, Ct))!.Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected_by_the_api()
    {
        var foreign = Generator(secret: "another-signing-key-that-is-also-32-chars-long").CreateToken(UserClaims);
        (await GetSecureAsync(foreign.access_token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_expired_but_validly_signed_token_issues_a_working_token()
    {
        var login = await Host.LoginAsync();
        var expired = Generator(now: DateTimeOffset.UtcNow.AddHours(-2)).CreateToken(UserClaims);

        (await GetSecureAsync(expired.access_token)).Should().Be(HttpStatusCode.Unauthorized, "the access token itself has expired");

        using var response = await Host.Client.PostAsJsonAsync(
            "api/v1/token/refreshtoken",
            new TokenRefreshRequestModel { token = expired.access_token, refresh_token = login.refresh_token },
            TestApiHost.Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, Ct);
        refreshed!.Result.Should().Be(ResultEnum.Success);
        refreshed.refresh_token.Should().NotBe(login.refresh_token, "refresh tokens are rotated");
        (await GetSecureAsync(refreshed.access_token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_with_token_signed_by_another_key_is_rejected()
    {
        var login = await Host.LoginAsync();
        var forged = Generator(secret: "another-signing-key-that-is-also-32-chars-long", now: DateTimeOffset.UtcNow.AddHours(-2)).CreateToken(UserClaims);

        using var response = await Host.Client.PostAsJsonAsync(
            "api/v1/token/refreshtoken",
            new TokenRefreshRequestModel { token = forged.access_token, refresh_token = login.refresh_token },
            TestApiHost.Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, Ct))!.Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Refresh_with_unknown_refresh_token_is_rejected()
    {
        var login = await Host.LoginAsync();

        using var response = await Host.Client.PostAsJsonAsync(
            "api/v1/token/refreshtoken",
            new TokenRefreshRequestModel { token = login.access_token, refresh_token = "not-the-stored-one" },
            TestApiHost.Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPrincipalFromExpiredTokenAsync_returns_null_for_garbage()
    {
        (await Generator().GetPrincipalFromExpiredTokenAsync("garbage")).Should().BeNull();
    }
}

public sealed class RateLimitingTests
{
    [Fact]
    public async Task Token_endpoint_returns_429_after_TokenRequestsPerMinute()
    {
        await using var host = await TestApiHost.StartAsync(configure: o => o.TokenRequestsPerMinute = 2);
        var ct = TestContext.Current.CancellationToken;
        var request = new LoginRequestModel { username = "alice", password = "wrong" };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await host.Client.PostAsJsonAsync("api/v1/token/login", request, TestApiHost.Json, ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);

        // Non-token endpoints are not rate limited by the token policy.
        using var health = await host.Client.GetAsync("/health", ct);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_endpoint_is_not_rate_limited()
    {
        await using var host = await TestApiHost.StartAsync(configure: o => o.TokenRequestsPerMinute = 1);
        var ct = TestContext.Current.CancellationToken;

        var token = await host.LoginAsync();
        for (var i = 0; i < 4; i++)
        {
            using var response = await host.RefreshAsync(token.access_token, token.refresh_token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            token = (await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, ct))!;
        }

        using var login = await host.Client.PostAsJsonAsync("api/v1/token/login", new LoginRequestModel { username = "alice", password = "x" }, TestApiHost.Json, ct);
        login.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the login budget (1) was used by the first login");
    }

    [Fact]
    public async Task Login_limit_is_partitioned_by_client_ip()
    {
        await using var host = await TestApiHost.StartAsync(configure: o => o.TokenRequestsPerMinute = 1);
        var ct = TestContext.Current.CancellationToken;

        async Task<HttpStatusCode> LoginFromAsync(string ip)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/token/login")
            {
                Content = JsonContent.Create(new LoginRequestModel { username = "alice", password = "wrong" }, options: TestApiHost.Json),
            };
            request.Headers.Add(TestRemoteIpStartupFilter.Header, ip);
            using var response = await host.Client.SendAsync(request, ct);
            return response.StatusCode;
        }

        (await LoginFromAsync("203.0.113.1")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFromAsync("203.0.113.1")).Should().Be(HttpStatusCode.TooManyRequests);
        (await LoginFromAsync("203.0.113.2")).Should().Be(HttpStatusCode.Unauthorized);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
