using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Enumerations;

namespace Middt.Framework.Tests.Api;

public sealed class CrudEndpointTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    private HttpClient Client => fixture.Host.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetItems_filters_sorts_and_pages()
    {
        await fixture.Host.SeedAsync([.. Enumerable.Range(1, 12).Select(i => new ApiItem { Name = $"Page{i:00}", Quantity = 100 - i })]);

        using var response = await Client.PostAsJsonAsync(
            "api/v1/items/getitems",
            new BaseSearchRequestModel<ApiItem>
            {
                RequestModel = new ApiItem { Name = "Page" },
                SortProperty = "quantity",
                SortReverse = true, // ascending
                RequestItemSize = 5,
                CurrentPage = 3,
            },
            TestApiHost.Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseResponseDataModel<List<ApiItem>>>(TestApiHost.Json, Ct);
        body!.Result.Should().Be(ResultEnum.Success);
        body.Count.Should().Be(12);
        body.Data!.Select(i => i.Quantity).Should().Equal(98, 99);
    }

    [Fact]
    public async Task GetById_of_missing_row_returns_404_problem_details()
    {
        using var response = await Client.GetAsync("api/v1/items/getbyid?id=987654", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestApiHost.Json, Ct);
        problem!.Status.Should().Be(404);
        problem.Title.Should().Be("Kayıt bulunamadı.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("far-too-long-name")]
    public async Task Insert_with_invalid_model_returns_400_validation_problem(string name)
    {
        using var response = await Client.PostAsJsonAsync("api/v1/items/insert", new ApiItem { Name = name }, TestApiHost.Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestApiHost.Json, Ct);
        problem!.Errors.Keys.Should().Contain(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Insert_valid_model_returns_success_envelope_and_is_readable_by_id()
    {
        using var response = await Client.PostAsJsonAsync("api/v1/items/insert", new ApiItem { Name = "Widget", Quantity = 3 }, TestApiHost.Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseResponseDataModel<ApiItem>>(TestApiHost.Json, Ct);
        body!.Result.Should().Be(ResultEnum.Success);
        body.Count.Should().Be(1);
        body.Data!.Id.Should().BePositive();

        var byId = await Client.GetFromJsonAsync<BaseResponseDataModel<ApiItem>>($"api/v1/items/getbyid?id={body.Data.Id}", TestApiHost.Json, Ct);
        byId!.Data!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task Update_and_Delete_of_client_supplied_entity_work_end_to_end()
    {
        using var insert = await Client.PostAsJsonAsync("api/v1/items/insert", new ApiItem { Name = "ToChange", Quantity = 1 }, TestApiHost.Json, Ct);
        var id = (await insert.Content.ReadFromJsonAsync<BaseResponseDataModel<ApiItem>>(TestApiHost.Json, Ct))!.Data!.Id;

        using var update = await Client.PostAsJsonAsync("api/v1/items/update", new ApiItem { Id = id, Name = "Changed", Quantity = 2 }, TestApiHost.Json, Ct);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetFromJsonAsync<BaseResponseDataModel<ApiItem>>($"api/v1/items/getbyid?id={id}", TestApiHost.Json, Ct))!
            .Data!.Name.Should().Be("Changed");

        // Delete skips validation: an id-only payload (empty required Name) is accepted.
        using var delete = await Client.PostAsJsonAsync("api/v1/items/delete", new ApiItem { Id = id }, TestApiHost.Json, Ct);
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        using var gone = await Client.GetAsync($"api/v1/items/getbyid?id={id}", Ct);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Insert_violating_a_unique_index_maps_DbUpdateException_to_409()
    {
        using var first = await Client.PostAsJsonAsync("api/v1/items/insert", new ApiItem { Name = "Dup", Code = "DUP-1" }, TestApiHost.Json, Ct);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await Client.PostAsJsonAsync("api/v1/items/insert", new ApiItem { Name = "Dup", Code = "DUP-1" }, TestApiHost.Json, Ct);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        using var response = await Client.GetAsync("/health", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be("Healthy");
    }
}
