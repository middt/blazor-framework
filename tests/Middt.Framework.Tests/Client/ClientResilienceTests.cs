using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Middt.Framework.Client.DependencyInjection;
using Middt.Framework.Client.Http;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Tests.Client;

/// <summary>
/// Resilience pipeline of <c>AddMiddtApiClient</c>: only safe methods are retried, 429 never, the token client never, and
/// the workflow client gets a longer attempt timeout.
/// </summary>
public sealed class ClientResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class ProbeClient(HttpClient http)
    {
        public HttpClient Http => http;
    }

    private static ServiceProvider Build(FakeHttpHandler handler, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiSettings:URL"] = "http://api.test", ["ApiSettings:Version"] = "1" })
            .Build();
        services.AddMiddtApiClients(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddMiddtApiClient<ProbeClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        register?.Invoke(services);
        // No back-off delays in tests.
        services.PostConfigureAll<HttpStandardResilienceOptions>(o => o.Retry.Delay = TimeSpan.Zero);
        return services.BuildServiceProvider();
    }

    private static async Task<int> CallsAsync(HttpMethod method, HttpStatusCode status)
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status));
        await using var provider = Build(handler);
        var client = provider.GetRequiredService<ProbeClient>();

        using var request = new HttpRequestMessage(method, "api/v1/items") { Content = method == HttpMethod.Get ? null : new StringContent("{}") };
        using var response = await client.Http.SendAsync(request, Ct);

        response.StatusCode.Should().Be(status);
        return handler.Requests.Count;
    }

    [Fact]
    public async Task Safe_methods_are_retried_on_transient_errors() =>
        (await CallsAsync(HttpMethod.Get, HttpStatusCode.ServiceUnavailable)).Should().BeGreaterThan(1);

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Unsafe_methods_are_never_resent(string method) =>
        (await CallsAsync(new HttpMethod(method), HttpStatusCode.ServiceUnavailable)).Should().Be(1);

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Too_many_requests_is_never_retried(string method) =>
        (await CallsAsync(new HttpMethod(method), HttpStatusCode.TooManyRequests)).Should().Be(1);

    [Fact]
    public async Task Token_client_is_not_retried()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await using var provider = Build(handler);
        var tokens = provider.GetRequiredService<ITokenService>();

        var result = await tokens.LoginAsync(new LoginRequestModel { username = "alice", password = "x" }, Ct);

        result.IsSuccess.Should().BeFalse();
        handler.Requests.Should().ContainSingle("a retried login consumes the login limiter's permits");
    }

    [Fact]
    public async Task Workflow_client_has_a_longer_attempt_timeout()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var provider = Build(handler, services => services.AddMiddtApiClient<WorkflowApiClient>());
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();

        options.Get("WorkflowApiClient-standard").AttemptTimeout.Timeout.Should().Be(ClientServiceCollectionExtensions.WorkflowAttemptTimeout);
        options.Get("ProbeClient-standard").AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(10), "other clients keep the standard timeout");
    }
}
