using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Tests.Infrastructure;

namespace Middt.Framework.Tests.Api;

/// <summary>Refresh token rotation, reuse detection, revocation and login hardening of BaseTokenController.</summary>
public sealed class TokenSecurityTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    private const string GenericLoginError = "Kullanıcı adı veya şifreniz hatalı.";

    private TestApiHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<TokenResponseModel> RefreshOkAsync(TokenResponseModel current)
    {
        using var response = await Host.RefreshAsync(current.access_token, current.refresh_token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponseModel>(TestApiHost.Json, Ct))!;
    }

    private async Task<HttpStatusCode> RefreshStatusAsync(string? accessToken, string? refreshToken)
    {
        using var response = await Host.RefreshAsync(accessToken, refreshToken);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> RevokeAsync(string? bearer, string? refreshToken)
    {
        using var request = TestApiHost.Authorized(HttpMethod.Post, "api/v1/token/revoke", bearer);
        request.Content = JsonContent.Create(new TokenRevokeRequestModel { refresh_token = refreshToken }, options: TestApiHost.Json);
        using var response = await Host.Client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token_and_the_old_one_is_rejected()
    {
        var login = await Host.LoginAsync();

        var rotated = await RefreshOkAsync(login);

        rotated.refresh_token.Should().NotBe(login.refresh_token);
        (await RefreshStatusAsync(login.access_token, login.refresh_token)).Should().Be(HttpStatusCode.Unauthorized, "refresh tokens are single use");
    }

    [Fact]
    public async Task Rotated_tokens_can_be_chained()
    {
        var token = await Host.LoginAsync();
        for (var i = 0; i < 3; i++)
        {
            token = await RefreshOkAsync(token);
        }

        token.Username.Should().Be(FakeUserStore.Username);
    }

    [Fact]
    public async Task Reuse_of_an_old_refresh_token_revokes_the_newer_one_too()
    {
        var login = await Host.LoginAsync();
        var rotated = await RefreshOkAsync(login);

        // An attacker replays the stolen original token.
        (await RefreshStatusAsync(login.access_token, login.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);

        // The legitimate client's current token belongs to the revoked family.
        (await RefreshStatusAsync(rotated.access_token, rotated.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reuse_does_not_affect_other_sessions_of_the_same_user()
    {
        var session1 = await Host.LoginAsync();
        var session2 = await Host.LoginAsync();

        await RefreshOkAsync(session1);
        (await RefreshStatusAsync(session1.access_token, session1.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);

        await RefreshOkAsync(session2);
    }

    [Fact]
    public async Task Refresh_token_presented_with_another_users_access_token_is_rejected_and_its_family_revoked()
    {
        var alice = await Host.LoginAsync();
        var bob = await Host.LoginAsync(FakeUserStore.OtherUsername);

        (await RefreshStatusAsync(alice.access_token, bob.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);

        (await RefreshStatusAsync(bob.access_token, bob.refresh_token)).Should().Be(HttpStatusCode.Unauthorized, "a leaked refresh token's family is revoked");
        await RefreshOkAsync(alice);
    }

    [Fact]
    public async Task Disabled_user_cannot_refresh_and_the_family_stays_revoked()
    {
        var carol = await Host.LoginAsync(FakeUserStore.DisposableUsername);
        var users = Host.App.Services.GetRequiredService<FakeUserStore>();

        users.Disabled[FakeUserStore.DisposableUserId] = true;
        try
        {
            (await RefreshStatusAsync(carol.access_token, carol.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            users.Disabled.TryRemove(FakeUserStore.DisposableUserId, out _);
        }

        (await RefreshStatusAsync(carol.access_token, carol.refresh_token)).Should().Be(HttpStatusCode.Unauthorized, "re-enabling does not resurrect the session");
    }

    [Fact]
    public async Task Refresh_without_tokens_returns_400()
    {
        (await RefreshStatusAsync(null, "x")).Should().Be(HttpStatusCode.BadRequest);
        (await RefreshStatusAsync("x", null)).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoke_of_own_refresh_token_ends_the_session()
    {
        var login = await Host.LoginAsync();

        (await RevokeAsync(login.access_token, login.refresh_token)).Should().Be(HttpStatusCode.NoContent);

        (await RefreshStatusAsync(login.access_token, login.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_of_a_rotated_token_ends_the_whole_family()
    {
        var login = await Host.LoginAsync();
        var rotated = await RefreshOkAsync(login);

        (await RevokeAsync(rotated.access_token, login.refresh_token)).Should().Be(HttpStatusCode.NoContent);

        (await RefreshStatusAsync(rotated.access_token, rotated.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_of_someone_elses_token_returns_204_but_leaves_it_untouched()
    {
        var alice = await Host.LoginAsync();
        var bob = await Host.LoginAsync(FakeUserStore.OtherUsername);

        (await RevokeAsync(alice.access_token, bob.refresh_token)).Should().Be(HttpStatusCode.NoContent);

        await RefreshOkAsync(bob);
    }

    [Theory]
    [InlineData("unknown-refresh-token")]
    [InlineData("")]
    public async Task Revoke_of_unknown_or_empty_token_returns_204(string refreshToken)
    {
        var alice = await Host.LoginAsync();

        (await RevokeAsync(alice.access_token, refreshToken)).Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Revoke_without_authentication_returns_401_and_does_not_revoke()
    {
        var login = await Host.LoginAsync();

        (await RevokeAsync(null, login.refresh_token)).Should().Be(HttpStatusCode.Unauthorized);

        await RefreshOkAsync(login);
    }

    [Fact]
    public async Task Login_failure_is_identical_for_unknown_user_and_wrong_password()
    {
        async Task<(HttpStatusCode Status, string Body)> LoginAsync(string username, string password)
        {
            using var response = await Host.Client.PostAsJsonAsync(
                "api/v1/token/login", new LoginRequestModel { username = username, password = password }, TestApiHost.Json, Ct);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        }

        var unknownUser = await LoginAsync("no-such-user", "whatever");
        var wrongPassword = await LoginAsync(FakeUserStore.Username, "wrong");

        unknownUser.Status.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.Status.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.Body.Should().Be(wrongPassword.Body);

        var body = System.Text.Json.JsonSerializer.Deserialize<TokenResponseModel>(unknownUser.Body, TestApiHost.Json)!;
        body.Result.Should().Be(ResultEnum.Error);
        body.MessageList.Should().Equal(GenericLoginError);
    }

    [Theory]
    [InlineData(FakeUserStore.Username, FakeUserStore.Password, HttpStatusCode.OK)]
    [InlineData(FakeUserStore.Username, "wrong", HttpStatusCode.Unauthorized)]
    public async Task Token_responses_are_not_cacheable(string username, string password, HttpStatusCode expected)
    {
        using var response = await Host.Client.PostAsJsonAsync(
            "api/v1/token/login", new LoginRequestModel { username = username, password = password }, TestApiHost.Json, Ct);

        response.StatusCode.Should().Be(expected);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_responses_are_not_cacheable()
    {
        var login = await Host.LoginAsync();

        using var response = await Host.RefreshAsync(login.access_token, login.refresh_token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}

public sealed class TokenLoggingTests
{
    [Fact]
    public async Task Failed_login_is_logged_with_EventId_1001_without_the_password()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestApiHost.StartAsync(configureBuilder: b => b.Logging.AddProvider(logs));
        var ct = TestContext.Current.CancellationToken;

        using var response = await host.Client.PostAsJsonAsync(
            "api/v1/token/login", new LoginRequestModel { username = "mallory", password = "Secret-Guess-123" }, TestApiHost.Json, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var failure = logs.Entries.Should().ContainSingle(e => e.EventId.Id == 1001).Subject;
        failure.Level.Should().Be(LogLevel.Warning);
        failure.Message.Should().Contain("mallory");
        logs.Entries.Should().NotContain(e => e.Message.Contains("Secret-Guess-123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_token_reuse_is_logged_with_EventId_1003()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestApiHost.StartAsync(configureBuilder: b => b.Logging.AddProvider(logs));

        var login = await host.LoginAsync();
        (await host.RefreshAsync(login.access_token, login.refresh_token)).Dispose();
        (await host.RefreshAsync(login.access_token, login.refresh_token)).Dispose();

        logs.Entries.Should().ContainSingle(e => e.EventId.Id == 1003).Which.Level.Should().Be(LogLevel.Warning);
    }
}
