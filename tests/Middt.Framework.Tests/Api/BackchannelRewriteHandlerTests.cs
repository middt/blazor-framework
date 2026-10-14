using System.Net;
using Middt.Framework.Common.Http;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Api;

public sealed class BackchannelRewriteHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://localhost:8083/.well-known/openid-configuration", "http://middt-identity:8080/.well-known/openid-configuration")]
    [InlineData("http://localhost:8083/connect/token?x=a%20b&y=1", "http://middt-identity:8080/connect/token?x=a%20b&y=1")]
    [InlineData("http://localhost:8083/", "http://middt-identity:8080/")]
    [InlineData("http://LOCALHOST:8083/jwks", "http://middt-identity:8080/jwks")]
    public void Public_authority_is_rewritten_keeping_path_and_query(string input, string expected)
    {
        using var handler = new BackchannelRewriteHandler("http://localhost:8083/", "http://middt-identity:8080/");

        handler.Rewrite(new Uri(input)).AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:8084/connect/token")]
    [InlineData("https://localhost:8083/connect/token")]
    [InlineData("http://example.com:8083/connect/token")]
    public void Other_authorities_are_left_alone(string input)
    {
        using var handler = new BackchannelRewriteHandler("http://localhost:8083/", "http://middt-identity:8080/");

        handler.Rewrite(new Uri(input)).AbsoluteUri.Should().Be(input);
    }

    [Theory]
    [InlineData("https://idp.example/realms/a/protocol/token", "http://idp:8080/auth/realms/a/protocol/token")]
    [InlineData("https://idp.example/realms/a", "http://idp:8080/auth/realms/a")]
    public void Authorities_with_a_path_rewrite_the_prefix(string input, string expected)
    {
        using var handler = new BackchannelRewriteHandler("https://idp.example/realms/a", "http://idp:8080/auth/realms/a/");

        handler.Rewrite(new Uri(input)).AbsoluteUri.Should().Be(expected);
        handler.Rewrite(new Uri("https://idp.example/realms/ab/x")).AbsoluteUri.Should().Be("https://idp.example/realms/ab/x", "only whole path segments match");
    }

    [Theory]
    [InlineData(null, "http://internal/")]
    [InlineData("http://localhost:8083/", null)]
    [InlineData("", "")]
    public void Without_both_authorities_requests_pass_through(string? publicAuthority, string? backchannel)
    {
        using var handler = new BackchannelRewriteHandler(publicAuthority, backchannel);

        handler.IsEnabled.Should().BeFalse();
        handler.Rewrite(new Uri("http://localhost:8083/x")).AbsoluteUri.Should().Be("http://localhost:8083/x");
    }

    [Fact]
    public void Relative_or_non_http_authorities_are_rejected()
    {
        var act = () => new BackchannelRewriteHandler("localhost:8083", "http://internal/");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Requests_are_sent_to_the_backchannel_authority()
    {
        var inner = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new BackchannelRewriteHandler("http://localhost:8083/", "http://idp:8080/", inner));

        using var response = await client.GetAsync(new Uri("http://localhost:8083/connect/userinfo?a=1"), Ct);

        response.RequestMessage!.RequestUri!.AbsoluteUri.Should().Be("http://idp:8080/connect/userinfo?a=1");
        inner.Requests.Should().ContainSingle().Which.Uri.Should().Be("/connect/userinfo?a=1");
    }
}
