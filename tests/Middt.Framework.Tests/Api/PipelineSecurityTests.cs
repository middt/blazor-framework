using System.Net;
using System.Net.Http.Json;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Tests.Api;

internal static class PipelineRequests
{
    public static HttpRequestMessage FromIp(HttpMethod method, string uri, string remoteIp, string? forwardedFor = null, string? forwardedProto = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(TestRemoteIpStartupFilter.Header, remoteIp);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return request;
    }

    public static async Task<(string? Ip, string Scheme)> ClientInfoAsync(HttpClient client, string remoteIp, string? forwardedFor = null, string? forwardedProto = null)
    {
        using var request = FromIp(HttpMethod.Get, "api/v1/clientinfo/ip", remoteIp, forwardedFor, forwardedProto);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClientInfo>(TestApiHost.Json, TestContext.Current.CancellationToken);
        return (body!.Ip, body.Scheme);
    }

    public static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "api/v1/items/getall");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public sealed record ClientInfo(string? Ip, string Scheme);
}

public sealed class SecurityHeaderTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health")]
    [InlineData("api/v1/items/getall")]
    [InlineData("api/v1/secureitems/getall")] // 401
    [InlineData("api/v1/items/getbyid?id=424242")] // 404 problem details
    public async Task Api_responses_carry_the_owasp_security_headers(string uri)
    {
        using var response = await fixture.Host.Client.GetAsync(uri, Ct);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("strict-origin-when-cross-origin");
        response.Headers.GetValues("Permissions-Policy").Single().Should().Contain("camera=()");
        response.Headers.GetValues("Cross-Origin-Opener-Policy").Should().Equal("same-origin");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("default-src 'none'").And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Scalar_ui_gets_no_api_csp_but_still_the_other_headers()
    {
        using var response = await fixture.Host.Client.GetAsync("/scalar/v1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [Fact]
    public async Task Paths_that_merely_start_with_scalar_still_get_the_csp()
    {
        using var response = await fixture.Host.Client.GetAsync("/scalarx", Ct);

        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task No_hsts_in_Development()
    {
        using var response = await fixture.Host.Client.GetAsync("https://api.example.com/health", Ct);

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }
}

public sealed class ProductionSecurityHeaderTests(ProductionApiFixture fixture) : IClassFixture<ProductionApiFixture>
{
    [Fact]
    public async Task Hsts_is_sent_over_https_outside_Development()
    {
        using var response = await fixture.Host.Client.GetAsync("https://api.example.com/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Strict-Transport-Security").Single().Should().Contain("max-age=");
    }

    [Fact]
    public async Task Hsts_is_not_sent_over_plain_http()
    {
        using var response = await fixture.Host.Client.GetAsync("http://api.example.com/health", TestContext.Current.CancellationToken);

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }
}

public sealed class CorsTests
{
    private const string Allowed = "https://app.example.com";
    private const string Other = "https://evil.example.com";

    private static Task<TestApiHost> StartAsync(params string[] origins)
    {
        var settings = new Dictionary<string, string?> { ["CorsSettings:CorsModelList:0:Name"] = "default" };
        for (var i = 0; i < origins.Length; i++)
        {
            settings[$"CorsSettings:CorsModelList:0:Url:{i}"] = origins[i];
        }

        return TestApiHost.StartAsync(settings: settings);
    }

    [Fact]
    public async Task Preflight_from_a_listed_origin_is_allowed_with_credentials()
    {
        await using var host = await StartAsync(Allowed);

        using var response = await PipelineRequests.PreflightAsync(host.Client, Allowed);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(Allowed);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");
    }

    [Fact]
    public async Task Preflight_from_an_unlisted_origin_gets_no_cors_headers()
    {
        await using var host = await StartAsync(Allowed);

        using var response = await PipelineRequests.PreflightAsync(host.Client, Other);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Simple_request_from_an_unlisted_origin_gets_no_cors_headers()
    {
        await using var host = await StartAsync(Allowed);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/items/getall");
        request.Headers.Add("Origin", Other);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Policy_without_origins_allows_nothing()
    {
        await using var host = await StartAsync();

        using var allowed = await PipelineRequests.PreflightAsync(host.Client, Allowed);
        using var other = await PipelineRequests.PreflightAsync(host.Client, Other);

        allowed.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        other.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Wildcard_allows_any_origin_but_never_credentials()
    {
        await using var host = await StartAsync("*");

        using var response = await PipelineRequests.PreflightAsync(host.Client, Other);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Without_cors_configuration_no_cors_headers_are_sent()
    {
        await using var host = await TestApiHost.StartAsync();

        using var response = await PipelineRequests.PreflightAsync(host.Client, Allowed);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}

public sealed class ForwardedHeadersTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Forwarded_headers_from_loopback_are_honoured(string proxy)
    {
        var (ip, scheme) = await PipelineRequests.ClientInfoAsync(fixture.Host.Client, proxy, "198.51.100.7", "https");

        ip.Should().Be("198.51.100.7");
        scheme.Should().Be("https");
    }

    [Fact]
    public async Task Forwarded_headers_from_an_untrusted_address_are_ignored()
    {
        var (ip, scheme) = await PipelineRequests.ClientInfoAsync(fixture.Host.Client, "203.0.113.9", "198.51.100.7", "https");

        ip.Should().Be("203.0.113.9");
        scheme.Should().Be("http");
    }

    [Fact]
    public async Task Without_forwarded_headers_the_socket_address_is_used()
    {
        var (ip, _) = await PipelineRequests.ClientInfoAsync(fixture.Host.Client, "203.0.113.10");

        ip.Should().Be("203.0.113.10");
    }
}

public sealed class TrustedProxyFixture : IAsyncLifetime
{
    public TestApiHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Host = await TestApiHost.StartAsync(
            configure: o => o.TokenRequestsPerMinute = 1,
            settings: new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
                ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
            });

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

public sealed class TrustedProxyTests(TrustedProxyFixture fixture) : IClassFixture<TrustedProxyFixture>
{
    [Theory]
    [InlineData("10.0.0.5", "198.51.100.7")]
    [InlineData("10.1.2.3", "198.51.100.7")]
    [InlineData("127.0.0.1", "198.51.100.7")] // loopback stays trusted
    [InlineData("10.0.0.6", "10.0.0.6")]
    [InlineData("10.2.0.1", "10.2.0.1")]
    public async Task Only_configured_proxies_and_networks_may_set_the_client_ip(string remoteIp, string expectedClientIp)
    {
        var (ip, _) = await PipelineRequests.ClientInfoAsync(fixture.Host.Client, remoteIp, "198.51.100.7");

        ip.Should().Be(expectedClientIp);
    }

    [Fact]
    public async Task Login_rate_limit_partitions_by_forwarded_client_ip_behind_a_trusted_proxy_only()
    {
        var ct = TestContext.Current.CancellationToken;

        async Task<HttpStatusCode> LoginAsync(string remoteIp, string forwardedFor)
        {
            using var request = PipelineRequests.FromIp(HttpMethod.Post, "api/v1/token/login", remoteIp, forwardedFor);
            request.Content = JsonContent.Create(new LoginRequestModel { username = "alice", password = "wrong" }, options: TestApiHost.Json);
            using var response = await fixture.Host.Client.SendAsync(request, ct);
            return response.StatusCode;
        }

        // Behind the trusted proxy every end user has an own budget (1 per minute).
        (await LoginAsync("10.0.0.5", "198.51.100.1")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("10.0.0.5", "198.51.100.1")).Should().Be(HttpStatusCode.TooManyRequests);
        (await LoginAsync("10.0.0.5", "198.51.100.2")).Should().Be(HttpStatusCode.Unauthorized);

        // A direct client cannot escape its budget by spoofing X-Forwarded-For.
        (await LoginAsync("203.0.113.50", "198.51.100.3")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("203.0.113.50", "198.51.100.4")).Should().Be(HttpStatusCode.TooManyRequests);
    }
}
