using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Tests.Api;

namespace Middt.Framework.Tests.Client;

public sealed class RefreshSingleFlightTests
{
    private const int Concurrency = 5;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Ok() => FakeHttpHandler.Json(HttpStatusCode.OK, BaseResponseDataModel<List<ApiItem>>.Success([], 0));

    /// <summary>Answers 401 to the stale token; the refresh only completes once every concurrent request has seen its 401.</summary>
    private static (FakeHttpHandler Handler, TaskCompletionSource AllUnauthorized) StaleTokenApi()
    {
        var unauthorized = 0;
        var allUnauthorized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Headers.Authorization?.Parameter == "fresh")
            {
                return Ok();
            }

            if (Interlocked.Increment(ref unauthorized) == Concurrency)
            {
                allUnauthorized.TrySetResult();
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        return (handler, allUnauthorized);
    }

    [Fact]
    public async Task Concurrent_401s_share_a_single_refresh_call()
    {
        var (handler, allUnauthorized) = StaleTokenApi();
        using var _ = handler;
        using var http = handler.CreateClient();
        var refreshToken = "rt-" + Guid.NewGuid().ToString("N");
        var session = new CountingSessionState("stale", refreshToken);
        var tokens = new FakeTokenService(async (_, _) =>
        {
            await allUnauthorized.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            return new TokenResponseModel { Result = ResultEnum.Success, access_token = "fresh", refresh_token = "rt-next-" + Guid.NewGuid().ToString("N") };
        });

        var results = await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            Task.Run(() => new ItemsApiClient(http, session, tokens).GetAllAsync(Ct), Ct)));

        results.Should().OnlyContain(r => r.IsSuccess);
        tokens.RefreshCalls.Should().Be(1, "rotated refresh tokens are single use: a second refresh would trigger reuse detection");
        tokens.RefreshRequests.Single().refresh_token.Should().Be(refreshToken);
        session.ClearCalls.Should().Be(0);
        handler.Requests.Count(r => r.Bearer == "fresh").Should().Be(Concurrency);
    }

    [Fact]
    public async Task Concurrent_401s_with_failing_refresh_call_refresh_once_and_clear_the_session()
    {
        var (handler, allUnauthorized) = StaleTokenApi();
        using var _ = handler;
        using var http = handler.CreateClient();
        var session = new CountingSessionState("stale", "rt-" + Guid.NewGuid().ToString("N"));
        var tokens = new FakeTokenService(async (_, _) =>
        {
            await allUnauthorized.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            return new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["revoked"] };
        });

        var results = await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            Task.Run(() => new ItemsApiClient(http, session, tokens).GetAllAsync(Ct), Ct)));

        results.Should().OnlyContain(r => r.Result == ResultEnum.Error);
        tokens.RefreshCalls.Should().Be(1);
        session.ClearCalls.Should().Be(1);
        (await session.Token()).Should().BeEmpty();
        handler.Requests.Should().HaveCount(Concurrency, "nothing is retried after a failed refresh");
    }

    [Fact]
    public async Task Request_that_read_the_session_before_a_completed_refresh_does_not_replay_the_rotated_token()
    {
        // Regression: B reads (stale, rt1), A refreshes (rt1 -> rt2) and removes its in-flight entry, then B starts its
        // own refresh with rt1. Against the real API that replay is reuse detection: the whole session is revoked.
        var rt1 = "rt-" + Guid.NewGuid().ToString("N");
        var session = new PausingSessionState("stale", rt1);
        using var handler = new FakeHttpHandler(request =>
            request.Headers.Authorization?.Parameter == "fresh" ? Ok() : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var tokens = new FakeTokenService(request => request.refresh_token == rt1
            ? new TokenResponseModel { Result = ResultEnum.Success, access_token = "fresh", refresh_token = "rt2-" + Guid.NewGuid().ToString("N") }
            : new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["reuse detected"] });

        var requestB = session.RunPausedAsync(() => new ItemsApiClient(http, session, tokens).GetAllAsync(Ct));
        await session.Paused.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var resultA = await new ItemsApiClient(http, session, tokens).GetAllAsync(Ct);
        session.Resume();
        var resultB = await requestB.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();
        tokens.RefreshCalls.Should().Be(1);
        session.ClearCalls.Should().Be(0);
    }

    [Fact]
    public async Task Token_changed_by_another_request_is_retried_without_refreshing()
    {
        var session = new CountingSessionState("stale", "rt-" + Guid.NewGuid().ToString("N"));
        using var handler = new FakeHttpHandler(request =>
        {
            if (request.Headers.Authorization?.Parameter == "fresh")
            {
                return Ok();
            }

            // While this request was in flight another request of the session refreshed the token.
            session.SetToken(new TokenResponseModel { access_token = "fresh", refresh_token = "rt-other" }).GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using var http = handler.CreateClient();
        var tokens = new FakeTokenService(_ => throw new InvalidOperationException("must not refresh"));

        var result = await new ItemsApiClient(http, session, tokens).GetAllAsync(Ct);

        result.IsSuccess.Should().BeTrue();
        tokens.RefreshCalls.Should().Be(0);
        handler.Requests.Select(r => r.Bearer).Should().Equal("stale", "fresh");
    }

    [Fact]
    public async Task Refresh_failure_calls_ClearToken()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var session = new CountingSessionState("stale", "rt-" + Guid.NewGuid().ToString("N"));
        var tokens = new FakeTokenService(_ => new TokenResponseModel { Result = ResultEnum.Error });

        (await new ItemsApiClient(http, session, tokens).GetAllAsync(Ct)).Result.Should().Be(ResultEnum.Error);

        session.ClearCalls.Should().Be(1);
    }

    [Fact]
    public async Task Sequential_refreshes_with_different_refresh_tokens_are_not_coalesced()
    {
        var generation = 0;
        using var handler = new FakeHttpHandler(request =>
            request.Headers.Authorization?.Parameter == $"fresh-{Volatile.Read(ref generation)}" ? Ok() : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var session = new CountingSessionState("stale", "rt-" + Guid.NewGuid().ToString("N"));
        var tokens = new FakeTokenService(_ =>
        {
            var next = Interlocked.Increment(ref generation);
            return new TokenResponseModel { Result = ResultEnum.Success, access_token = $"fresh-{next}", refresh_token = "rt-" + Guid.NewGuid().ToString("N") };
        });
        var client = new ItemsApiClient(http, session, tokens);

        (await client.GetAllAsync(Ct)).IsSuccess.Should().BeTrue();
        await session.SetToken(new TokenResponseModel { access_token = "expired-again", refresh_token = await session.RefreshToken() });
        (await client.GetAllAsync(Ct)).IsSuccess.Should().BeTrue();

        tokens.RefreshCalls.Should().Be(2);
    }
}

public sealed class TokenRevocationClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BaseTokenService Create(HttpClient http) =>
        new(http, Options.Create(new ApiSettings { URL = "http://localhost", Version = "1" }), NullLoggers.For<BaseTokenService>());

    [Fact]
    public async Task RevokeAsync_posts_the_refresh_token_with_the_bearer_access_token()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var http = handler.CreateClient();

        await Create(http).RevokeAsync("access-1", "refresh-1", Ct);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be("/api/v1/token/revoke");
        request.Bearer.Should().Be("access-1");
        request.Body.Should().Be("{\"refresh_token\":\"refresh-1\"}");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RevokeAsync_swallows_error_statuses(HttpStatusCode status)
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status));
        using var http = handler.CreateClient();

        var act = () => Create(http).RevokeAsync("a", "r", Ct);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_swallows_network_failures()
    {
        using var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("down"));
        using var http = handler.CreateClient();

        await ((Func<Task>)(() => Create(http).RevokeAsync("a", "r", Ct))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_swallows_timeouts_and_resilience_rejections()
    {
        // Sign-out must complete even when the API is slow or the circuit breaker is open.
        using var slow = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var slowHttp = slow.CreateClient();
        slowHttp.Timeout = TimeSpan.FromMilliseconds(50);
        await ((Func<Task>)(() => Create(slowHttp).RevokeAsync("a", "r", Ct))).Should().NotThrowAsync();

        using var broken = new FakeHttpHandler((_, _) => throw new Polly.CircuitBreaker.BrokenCircuitException("open"));
        using var brokenHttp = broken.CreateClient();
        await ((Func<Task>)(() => Create(brokenHttp).RevokeAsync("a", "r", Ct))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_against_the_real_api_revokes_the_session()
    {
        await using var host = await TestApiHost.StartAsync();
        var service = new BaseTokenService(host.Client, Options.Create(new ApiSettings { URL = "http://localhost", Version = "1" }), NullLoggers.For<BaseTokenService>());
        var login = await service.LoginAsync(new LoginRequestModel { username = FakeUserStore.Username, password = FakeUserStore.Password }, Ct);

        await service.RevokeAsync(login.access_token!, login.refresh_token!, Ct);

        var refreshed = await service.RefreshTokenAsync(new TokenRefreshRequestModel { token = login.access_token, refresh_token = login.refresh_token }, Ct);
        refreshed.IsSuccess.Should().BeFalse();
    }
}

public sealed class ForwardedClientIpHandlerTests
{
    private static async Task<Dictionary<string, string>> SendAsync(IHttpContextAccessor accessor, Action<HttpRequestMessage>? configure = null)
    {
        using var inner = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = new ForwardedClientIpHandler(accessor) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://api/") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/token/login");
        configure?.Invoke(request);

        using var _ = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return inner.Headers.Single();
    }

    private static HttpContextAccessor WithClientIp(string? ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return new HttpContextAccessor { HttpContext = context };
    }

    [Fact]
    public async Task Adds_the_end_users_ip_from_the_current_request()
    {
        var headers = await SendAsync(WithClientIp("198.51.100.23"));

        headers["X-Forwarded-For"].Should().Be("198.51.100.23");
    }

    [Fact]
    public async Task Replaces_an_existing_forwarded_header()
    {
        var headers = await SendAsync(WithClientIp("198.51.100.23"), r => r.Headers.Add("X-Forwarded-For", "6.6.6.6"));

        headers["X-Forwarded-For"].Should().Be("198.51.100.23");
    }

    [Fact]
    public async Task Without_HttpContext_no_header_is_added()
    {
        var headers = await SendAsync(new HttpContextAccessor());

        headers.Should().NotContainKey("X-Forwarded-For");
    }

    [Fact]
    public async Task Without_remote_ip_no_header_is_added()
    {
        var headers = await SendAsync(WithClientIp(null));

        headers.Should().NotContainKey("X-Forwarded-For");
    }
}

/// <summary>
/// Session state that pauses the first refresh token read of one request (marked through an AsyncLocal) right after the
/// value was read, to reproduce "read before another request's refresh completed, act after it".
/// </summary>
internal sealed class PausingSessionState(string token, string refreshToken) : IBaseSessionState
{
    private static readonly AsyncLocal<bool> IsPausedRequest = new();
    private readonly CountingSessionState inner = new(token, refreshToken);
    private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int pauses;

    public Task Paused => paused.Task;

    public int ClearCalls => inner.ClearCalls;

    public Task<T> RunPausedAsync<T>(Func<Task<T>> action) => Task.Run(() =>
    {
        IsPausedRequest.Value = true;
        return action();
    });

    public void Resume() => resume.TrySetResult();

    public Task<string> Token() => inner.Token();

    public async Task<string> RefreshToken()
    {
        var value = await inner.RefreshToken();
        if (IsPausedRequest.Value && Interlocked.Increment(ref pauses) == 1)
        {
            paused.TrySetResult();
            await resume.Task;
        }

        return value;
    }

    public Task<int> ExpiresIn() => inner.ExpiresIn();

    public Task SetToken(TokenResponseModel tokenResponseModel) => inner.SetToken(tokenResponseModel);

    public Task ClearToken() => inner.ClearToken();
}
