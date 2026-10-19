using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Tests.Api;

namespace Middt.Framework.Tests.Client;

public sealed class ApiClientBaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Ok(ApiItem item) =>
        FakeHttpHandler.Json(HttpStatusCode.OK, BaseResponseDataModel<ApiItem>.Success(item, 1));

    private static FakeTokenService RefreshReturns(string accessToken) =>
        new(_ => new TokenResponseModel { Result = ResultEnum.Success, access_token = accessToken, refresh_token = "refresh-2" });

    [Fact]
    public async Task Attaches_bearer_token_and_builds_versioned_route()
    {
        using var handler = new FakeHttpHandler(_ => Ok(new ApiItem { Id = 1, Name = "One" }));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("token-1", "refresh-1"), RefreshReturns("unused"));

        var result = await client.GetByIdAsync(1, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("One");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be("/api/v1/items/getbyid?id=1");
        handler.Requests[0].Bearer.Should().Be("token-1");
    }

    [Fact]
    public async Task No_authorization_header_when_session_has_no_token()
    {
        using var handler = new FakeHttpHandler(_ => Ok(new ApiItem()));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState(), RefreshReturns("unused"));

        await client.GetAllAsync(Ct);

        handler.Requests[0].Bearer.Should().BeNull();
    }

    [Fact]
    public async Task Posts_body_as_camelCase_json()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, BaseResponseDataModel<List<ApiItem>>.Success([], 0)));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        await client.GetItemsAsync(new { SortProperty = "Name", RequestItemSize = 3 }, Ct);

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.Should().Be("/api/v1/items/getitems");
        handler.Requests[0].Body.Should().Contain("\"sortProperty\":\"Name\"").And.Contain("\"requestItemSize\":3");
    }

    [Fact]
    public async Task On_401_refreshes_once_stores_new_token_and_retries_with_it()
    {
        using var handler = new FakeHttpHandler(request => request.Headers.Authorization?.Parameter == "fresh"
            ? Ok(new ApiItem { Id = 9 })
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var session = new FakeSessionState("stale", "refresh-1");
        var tokens = RefreshReturns("fresh");
        var client = new ItemsApiClient(http, session, tokens);

        var result = await client.InsertAsync(new ApiItem { Name = "x" }, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(9);
        tokens.RefreshRequests.Should().ContainSingle();
        tokens.RefreshRequests[0].token.Should().Be("stale");
        tokens.RefreshRequests[0].refresh_token.Should().Be("refresh-1");
        session.CurrentToken.Should().Be("fresh");
        session.CurrentRefreshToken.Should().Be("refresh-2");
        handler.Requests.Select(r => r.Bearer).Should().Equal("stale", "fresh");
        handler.Requests[1].Body.Should().Be(handler.Requests[0].Body, "the retry resends the same payload");
    }

    [Fact]
    public async Task Retry_that_is_still_401_returns_error_without_looping()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var tokens = RefreshReturns("fresh");
        var client = new ItemsApiClient(http, new FakeSessionState("stale", "refresh-1"), tokens);

        var result = await client.GetAllAsync(Ct);

        result.Result.Should().Be(ResultEnum.Error);
        tokens.RefreshRequests.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refresh_failure_returns_error_without_retrying_and_clears_the_session()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var session = new FakeSessionState("stale", "refresh-1");
        var tokens = new FakeTokenService(_ => new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["expired"] });
        var client = new ItemsApiClient(http, session, tokens);

        var result = await client.GetAllAsync(Ct);

        result.Result.Should().Be(ResultEnum.Error);
        tokens.RefreshRequests.Should().ContainSingle();
        handler.Requests.Should().ContainSingle();
        session.CurrentToken.Should().BeEmpty("a rejected refresh token ends the session (ClearToken)");
        session.CurrentRefreshToken.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_refresh_token_no_refresh_is_attempted()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = handler.CreateClient();
        var tokens = RefreshReturns("fresh");
        var client = new ItemsApiClient(http, new FakeSessionState("stale"), tokens);

        (await client.GetAllAsync(Ct)).Result.Should().Be(ResultEnum.Error);
        tokens.RefreshRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidationProblemDetails_errors_are_mapped_to_MessageList()
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["Name"] = ["Name is required.", "Name is too long."],
            ["Quantity"] = ["Quantity must be positive."],
        })
        { Status = 400, Title = "One or more validation errors occurred." };

        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.BadRequest, problem, "application/problem+json"));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        var result = await client.InsertAsync(new ApiItem(), Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().BeEquivalentTo(["Name is required.", "Name is too long.", "Quantity must be positive."]);
    }

    [Fact]
    public async Task ProblemDetails_title_is_used_when_there_are_no_validation_errors()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            HttpStatusCode.NotFound, new ProblemDetails { Status = 404, Title = "Kayıt bulunamadı." }, "application/problem+json"));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        var result = await client.GetByIdAsync(42, Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().Equal("Kayıt bulunamadı.");
    }

    [Fact]
    public async Task Network_failure_returns_error_instead_of_throwing()
    {
        using var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        var result = await client.GetAllAsync(Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Open_circuit_breaker_returns_error_instead_of_throwing()
    {
        using var handler = new FakeHttpHandler((_, _) => throw new Polly.CircuitBreaker.BrokenCircuitException("circuit open"));
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        var result = await client.GetAllAsync(Ct);

        result.Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Malformed_json_returns_error_instead_of_throwing()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"),
        });
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        (await client.GetAllAsync(Ct)).Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Non_json_success_response_returns_error_instead_of_throwing()
    {
        // e.g. a reverse proxy / SSO gateway answering with an HTML page
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>login</html>", System.Text.Encoding.UTF8, "text/html"),
        });
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        (await client.GetAllAsync(Ct)).Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var handler = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = handler.CreateClient();
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = () => client.GetAllAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Timeout_without_caller_cancellation_returns_error()
    {
        using var handler = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = handler.CreateClient();
        http.Timeout = TimeSpan.FromMilliseconds(50);
        var client = new ItemsApiClient(http, new FakeSessionState("t"), RefreshReturns("unused"));

        (await client.GetAllAsync(Ct)).Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public void Serializer_uses_web_defaults()
    {
        JsonSerializer.Serialize(new ApiItem { Name = "a" }, JsonSerializerOptions.Web).Should().Contain("\"name\"");
    }
}
