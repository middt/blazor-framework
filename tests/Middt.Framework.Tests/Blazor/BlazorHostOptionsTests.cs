using System.Net;

namespace Middt.Framework.Tests.Blazor;

/// <summary>Opt-in/opt-out features of <c>AddMiddtBlazor</c>/<c>UseMiddtBlazor</c>: response compression.</summary>
public sealed class BlazorHostOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> GetAsync(BlazorTestHost host, string path, string encoding = "br, gzip")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", encoding);
        return await host.CreateBrowser().SendAsync(request);
    }

    [Fact]
    public async Task Dynamic_responses_are_compressed_but_account_pages_are_not()
    {
        await using var host = await BlazorTestHost.StartAsync();

        using var fallback = await GetAsync(host, "/some/page");
        fallback.StatusCode.Should().Be(HttpStatusCode.OK);
        fallback.Content.Headers.ContentEncoding.Should().Equal("br");

        using var gzip = await GetAsync(host, "/some/page", "gzip");
        gzip.Content.Headers.ContentEncoding.Should().Equal("gzip");

        // Antiforgery token next to reflected input: never compressed (BREACH).
        using var login = await GetAsync(host, "/Account/Login");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await login.Content.ReadAsStringAsync(Ct)).Should().Contain("<form");
    }

    [Fact]
    public async Task Compression_can_be_disabled()
    {
        await using var host = await BlazorTestHost.StartAsync(configure: o => o.UseResponseCompression = false);

        using var response = await GetAsync(host, "/some/page");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
    }
}
