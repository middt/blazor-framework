using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Middt.Framework.Tests.Api;

public sealed class ProductionErrorHandlingTests(ProductionApiFixture fixture) : IClassFixture<ProductionApiFixture>
{
    [Fact]
    public async Task Unhandled_exception_returns_500_problem_without_details_but_with_traceId()
    {
        using var response = await fixture.Host.Client.GetAsync("api/v1/faults/boom", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        raw.Should().NotContain(FaultsController.SecretMessage);
        raw.Should().NotContain(nameof(InvalidOperationException));

        var problem = System.Text.Json.JsonSerializer.Deserialize<ProblemDetails>(raw, TestApiHost.Json)!;
        problem.Status.Should().Be(500);
        problem.Title.Should().Be("Beklenmeyen bir hata oluştu.");
        problem.Detail.Should().BeNull();
        problem.Extensions.Should().ContainKey("traceId");
        problem.Extensions["traceId"]!.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unreadable_request_is_a_400_not_a_server_error()
    {
        using var response = await fixture.Host.Client.GetAsync("api/v1/faults/unreadable", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestApiHost.Json, TestContext.Current.CancellationToken);
        problem!.Title.Should().Be("İstek geçersiz.");
    }

    [Fact]
    public async Task KeyNotFoundException_maps_to_404()
    {
        using var response = await fixture.Host.Client.GetAsync("api/v1/faults/missing", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestApiHost.Json, TestContext.Current.CancellationToken);
        problem!.Detail.Should().BeNull();
    }
}

public sealed class DevelopmentErrorHandlingTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    [Fact]
    public async Task Unhandled_exception_exposes_message_in_Development()
    {
        using var response = await fixture.Host.Client.GetAsync("api/v1/faults/boom", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestApiHost.Json, TestContext.Current.CancellationToken);
        problem!.Detail.Should().Be(FaultsController.SecretMessage);
        problem.Extensions.Should().ContainKey("traceId");
    }
}
