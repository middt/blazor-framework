using System.Net;
using System.Text.Json;

namespace Middt.Framework.Tests.Api;

internal static class OpenApiDocument
{
    public static async Task<List<string>> GetPathsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("Test API");
        document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();

        return [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name.ToLowerInvariant())];
    }
}

public sealed class DevelopmentOpenApiTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    [Fact]
    public async Task Development_document_contains_all_versioned_operations()
    {
        var paths = await OpenApiDocument.GetPathsAsync(fixture.Host.Client);

        paths.Should().Contain("/api/v1/items/getitems");
        paths.Should().Contain("/api/v1/items/insert");
        paths.Should().Contain("/api/v1/secureitems/getall");
        paths.Should().Contain("/api/v1/token/login");
        paths.Should().Contain("/api/v1/token/refreshtoken");
    }

    [Fact]
    public async Task Document_is_generated_for_deep_bidirectional_entity_graphs()
    {
        // Regression: scaffolded EF entities exceeded System.Text.Json's default depth of 64 during schema generation (HTTP 500).
        var paths = await OpenApiDocument.GetPathsAsync(fixture.Host.Client);

        paths.Should().Contain("/api/v1/graph/root");
    }
}

public sealed class ProductionOpenApiTests(ProductionApiFixture fixture) : IClassFixture<ProductionApiFixture>
{
    [Fact]
    public async Task Production_document_only_contains_operations_tagged_ShownInRelease()
    {
        var paths = await OpenApiDocument.GetPathsAsync(fixture.Host.Client);

        paths.Should().Equal("/api/v1/token/login");
    }

    [Fact]
    public async Task Production_does_not_publish_openapi_or_scalar_by_default()
    {
        await using var host = await TestApiHost.StartAsync("Production");

        using var document = await host.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var scalar = await host.Client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        document.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        scalar.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
