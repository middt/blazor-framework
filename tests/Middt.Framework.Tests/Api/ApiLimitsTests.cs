using System.Net;
using System.Net.Http.Json;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Api;

/// <summary>Page size clamp of <c>getitems</c>, row cap of <c>getall</c> and response compression.</summary>
public sealed class ApiLimitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestApiHost> HostAsync(Action<Middt.Framework.Api.MiddtApiOptions>? configure = null)
    {
        var host = await TestApiHost.StartAsync(configure: o =>
        {
            o.MaxPageSize = 3;
            o.MaxGetAllItems = 4;
            configure?.Invoke(o);
        });
        await host.SeedAsync([.. Enumerable.Range(1, 10).Select(i => new ApiItem { Name = $"Item{i:00}", Quantity = i })]);
        return host;
    }

    private static async Task<BaseResponseDataModel<List<ApiItem>>> GetItemsAsync(TestApiHost host, int requestItemSize, int currentPage = 1)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "api/v1/items/getitems",
            new BaseSearchRequestModel<ApiItem> { RequestItemSize = requestItemSize, CurrentPage = currentPage },
            TestApiHost.Json,
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<List<ApiItem>>>(TestApiHost.Json, Ct))!;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100000)]
    public async Task GetItems_clamps_unbounded_or_huge_page_sizes_to_MaxPageSize(int requestItemSize)
    {
        await using var host = await HostAsync();

        var page = await GetItemsAsync(host, requestItemSize);

        page.Data.Should().HaveCount(3);
        page.Count.Should().Be(10, "the total count is not affected by paging");
    }

    [Fact]
    public async Task GetItems_keeps_smaller_page_sizes_and_paging()
    {
        await using var host = await HostAsync();

        var page = await GetItemsAsync(host, 2, currentPage: 2);

        page.Data!.Select(i => i.Quantity).Should().Equal(3, 4);
    }

    [Fact]
    public async Task GetAll_returns_at_most_MaxGetAllItems_rows()
    {
        await using var host = await HostAsync();

        var body = await host.Client.GetFromJsonAsync<BaseResponseDataModel<List<ApiItem>>>("api/v1/items/getall", TestApiHost.Json, Ct);

        body!.Data.Should().HaveCount(4);
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
