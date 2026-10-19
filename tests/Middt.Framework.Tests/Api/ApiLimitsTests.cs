using System.Net;
using System.Net.Http.Json;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Api;

/// <summary>Response compression of JSON and ProblemDetails responses.</summary>
public sealed class ApiLimitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestApiHost> HostAsync(Action<Middt.Framework.Api.MiddtApiOptions>? configure = null)
    {
        var host = await TestApiHost.StartAsync(configure: configure);
        await host.SeedAsync([.. Enumerable.Range(1, 4).Select(i => new ApiItem { Name = $"Item{i:00}", Quantity = i })]);
        return host;
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Json_responses_are_compressed_when_the_client_accepts_it(string encoding)
    {
        await using var host = await HostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/items/getall");
        request.Headers.AcceptEncoding.ParseAdd(encoding);

        using var response = await host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal(encoding);
        response.Headers.Vary.Should().Contain("Accept-Encoding");
    }

    [Fact]
    public async Task Problem_details_are_compressed_too()
    {
        await using var host = await HostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/items/getbyid?id=424242");
        request.Headers.AcceptEncoding.ParseAdd("br");

        using var response = await host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentEncoding.Should().Equal("br");
    }

    [Fact]
    public async Task Compression_can_be_disabled()
    {
        await using var host = await HostAsync(o => o.EnableResponseCompression = false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/items/getall");
        request.Headers.AcceptEncoding.ParseAdd("br");

        using var response = await host.Client.SendAsync(request, Ct);

        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<List<ApiItem>>>(TestApiHost.Json, Ct))!.Data.Should().HaveCount(4);
    }
}

