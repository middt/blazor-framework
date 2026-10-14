using System.Net;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Tests.Api;

namespace Middt.Framework.Tests.Client;

public sealed class BaseTokenServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BaseTokenService Create(HttpClient http) =>
        new(http, Options.Create(new ApiSettings { URL = "http://localhost", Version = "1" }), NullLoggers.For<BaseTokenService>());

    [Fact]
    public async Task Login_posts_to_token_route_and_returns_token()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            HttpStatusCode.OK, new TokenResponseModel { Result = ResultEnum.Success, access_token = "abc", refresh_token = "def" }));
        using var http = handler.CreateClient();

        var result = await Create(http).LoginAsync(new LoginRequestModel { username = "u", password = "p" }, Ct);

        result.IsSuccess.Should().BeTrue();
        result.access_token.Should().Be("abc");
        handler.Requests[0].Uri.Should().Be("/api/v1/token/login");
        handler.Requests[0].Body.Should().Contain("\"username\":\"u\"");
    }

    [Fact]
    public async Task Unauthorized_with_TokenResponseModel_body_returns_server_messages()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            HttpStatusCode.Unauthorized, new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["Kullanıcı adı veya şifreniz sistemde bulunamadı."] }));
        using var http = handler.CreateClient();

        var result = await Create(http).LoginAsync(new LoginRequestModel { username = "u", password = "bad" }, Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().Equal("Kullanıcı adı veya şifreniz sistemde bulunamadı.");
    }

    [Fact]
    public async Task Error_status_with_success_looking_body_is_still_an_error()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, new TokenResponseModel()));
        using var http = handler.CreateClient();

        var result = await Create(http).RefreshTokenAsync(new TokenRefreshRequestModel { token = "t", refresh_token = "r" }, Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().NotBeEmpty();
        handler.Requests[0].Uri.Should().Be("/api/v1/token/refreshtoken");
    }

    [Fact]
    public async Task Non_json_gateway_error_returns_error_instead_of_throwing()
    {
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>", System.Text.Encoding.UTF8, "text/html"),
        });
        using var http = handler.CreateClient();

        var result = await Create(http).LoginAsync(new LoginRequestModel { username = "u", password = "p" }, Ct);

        result.Result.Should().Be(ResultEnum.Error);
    }

    [Fact]
    public async Task Network_failure_returns_error()
    {
        using var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("down"));
        using var http = handler.CreateClient();

        var result = await Create(http).LoginAsync(new LoginRequestModel { username = "u", password = "p" }, Ct);

        result.Result.Should().Be(ResultEnum.Error);
    }

    [Theory]
    [InlineData("circuit")]
    [InlineData("timeout")]
    public async Task Resilience_rejections_return_error_so_the_login_page_does_not_fail(string kind)
    {
        using var handler = new FakeHttpHandler((_, _) => kind == "circuit"
            ? throw new Polly.CircuitBreaker.BrokenCircuitException("circuit open")
            : throw new Polly.Timeout.TimeoutRejectedException("pipeline timeout"));
        using var http = handler.CreateClient();

        var result = await Create(http).LoginAsync(new LoginRequestModel { username = "u", password = "p" }, Ct);

        result.Result.Should().Be(ResultEnum.Error);
        result.MessageList.Should().NotBeEmpty();
    }
}
