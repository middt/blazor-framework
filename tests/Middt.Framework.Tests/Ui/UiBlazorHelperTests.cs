using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Ui.Model;
using Middt.Framework.Tests.Client;
using ClientHttpHandler = Middt.Framework.Tests.Client.FakeHttpHandler;

namespace Middt.Framework.Tests.Ui;

public sealed class UiBlazorHelperTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UiField Field(string json) => UiField.Parse(JsonNode.Parse(json)!.AsObject());

    private static string? Validate(UiField field, string value) => field.Validate(new JsonObject { [field.Field] = JsonNode.Parse(value) });

    public sealed class SearchFilter
    {
        public string? Name { get; set; }

        public int? CategoryId { get; set; }
    }

    [Fact]
    public void Field_parse_reads_all_properties_with_defaults()
    {
        var plain = Field("""{ "field": "name" }""");
        var full = Field("""{ "field": "price", "label": "Fiyat", "type": "number", "required": true, "min": 0, "max": 99.5, "maxLength": 10, "placeholder": "0,00" }""");

        plain.Label.Should().Be("name");
        plain.Type.Should().Be("text");
        plain.Required.Should().BeFalse();
        full.Label.Should().Be("Fiyat");
        full.Type.Should().Be("number");
        full.Required.Should().BeTrue();
        full.Min.Should().Be(0m);
        full.Max.Should().Be(99.5m);
        full.MaxLength.Should().Be(10);
        full.Placeholder.Should().Be("0,00");
    }

    [Fact]
    public void Field_parse_reads_options_and_lookups()
    {
        var select = Field("""{ "field": "status", "type": "select", "options": [ "new", 5, { "value": "done", "text": "Bitti" }, { "value": 7 } ] }""");
        var picker = Field("""
            { "field": "categoryId", "type": "picker", "lookup": { "entity": "Category", "valueField": "categoryId", "textField": "categoryName", "title": "Seç",
              "columns": [ { "field": "categoryName", "title": "Ad" } ], "search": [ { "field": "categoryName" } ] } }
            """);

        select.Options.Select(o => (o.Key, o.Text)).Should().Equal(("new", "new"), ("5", "5"), ("done", "Bitti"), ("7", "7"));
        picker.Lookup!.Entity.Should().Be("Category");
        picker.Lookup.ValueField.Should().Be("categoryId");
        picker.Lookup.TextField.Should().Be("categoryName");
        picker.Lookup.Title.Should().Be("Seç");
        picker.Lookup.Columns.Single().Title.Should().Be("Ad");
        picker.Lookup.Search.Single().Field.Should().Be("categoryName");
    }

    [Fact]
    public void Required_fields_reject_missing_null_and_blank_values_except_checkboxes()
    {
        var required = Field("""{ "field": "name", "required": true }""");

        required.Validate([]).Should().Be("Bu alan zorunludur.");
        Validate(required, "null").Should().Be("Bu alan zorunludur.");
        Validate(required, "\"  \"").Should().Be("Bu alan zorunludur.");
        Validate(required, "\"Ada\"").Should().BeNull();
        Field("""{ "field": "active", "type": "checkbox", "required": true }""").Validate([]).Should().BeNull();
        Field("""{ "field": "name" }""").Validate([]).Should().BeNull();
    }

    [Fact]
    public void Number_fields_check_integer_and_range()
    {
        var integer = Field("""{ "field": "count", "type": "integer", "min": 1, "max": 10 }""");
        var number = Field("""{ "field": "price", "type": "number", "min": 0.5 }""");

        Validate(integer, "2.5").Should().Be("Tam sayı olmalıdır.");
        Validate(integer, "\"3\"").Should().BeNull("numeric strings are accepted");
        Validate(integer, "0").Should().StartWith("En az 1");
        Validate(integer, "11").Should().StartWith("En fazla 10");
        Validate(integer, "10").Should().BeNull();
        Validate(number, "0.25").Should().StartWith("En az");
        Validate(number, "2.25").Should().BeNull();
    }

    [Fact]
    public void Number_fields_check_values_created_in_code()
    {
        // the field editor writes JsonValue<long>/<decimal>, which TryGetValue<decimal> alone does not read
        var integer = Field("""{ "field": "year", "type": "integer", "min": 1900 }""");

        integer.Validate(new JsonObject { ["year"] = JsonValue.Create(1800L) }).Should().StartWith("En az 1900");
        integer.Validate(new JsonObject { ["year"] = JsonValue.Create(2024L) }).Should().BeNull();
        integer.Validate(new JsonObject { ["year"] = JsonValue.Create(1950.5d) }).Should().Be("Tam sayı olmalıdır.");
    }

    [Fact]
    public void Text_fields_check_max_length()
    {
        var text = Field("""{ "field": "code", "maxLength": 3 }""");
        var textarea = Field("""{ "field": "note", "type": "textarea", "maxLength": 3 }""");
        var select = Field("""{ "field": "status", "type": "select", "maxLength": 3, "options": ["toolong"] }""");

        Validate(text, "\"abcd\"").Should().Be("En fazla 3 karakter olabilir.");
        Validate(text, "\"abc\"").Should().BeNull();
        Validate(textarea, "\"abcd\"").Should().Be("En fazla 3 karakter olabilir.");
        Validate(select, "\"toolong\"").Should().BeNull("maxLength applies to text inputs only");
    }

    [Fact]
    public void Columns_parse_with_defaults_and_pascal_case_sort_property()
    {
        var columns = UiColumn.ParseAll(JsonNode.Parse("""
            [ { "field": "categoryName", "title": "Ad", "format": "N2", "width": "100px", "sortable": false, "template": "{{ row.x }}" },
              { "field": "id" }, "ignored", { "field": "" } ]
            """)!.AsArray());

        columns.Should().HaveCount(3);
        columns[0].SortProperty.Should().Be("CategoryName");
        columns[0].Title.Should().Be("Ad");
        columns[0].Sortable.Should().BeFalse();
        columns[0].Format.Should().Be("N2");
        columns[0].Width.Should().Be("100px");
        columns[0].Template.Should().Be("{{ row.x }}");
        columns[1].Title.Should().Be("id");
        columns[1].Sortable.Should().BeTrue();
        columns[1].SortProperty.Should().Be("Id");
        columns[2].SortProperty.Should().BeEmpty();
        UiColumn.ParseAll(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Search_request_matches_the_api_wire_format(bool ascending)
    {
        var request = UiDataClient.SearchRequest(new JsonObject { ["name"] = "bike", ["categoryId"] = 3 }, 2, 25, "CategoryName", ascending);

        var model = request.Deserialize<BaseSearchRequestModel<SearchFilter>>(JsonSerializerOptions.Web)!;

        model.CurrentPage.Should().Be(2);
        model.RequestItemSize.Should().Be(25);
        model.SortProperty.Should().Be("CategoryName");
        model.SortReverse.Should().Be(ascending, "the legacy wire format uses sortReverse = true for ascending");
        model.RequestModel.Name.Should().Be("bike");
        model.RequestModel.CategoryId.Should().Be(3);
    }

    private static UiDataClient DataClient(ClientHttpHandler handler) =>
        new(handler.CreateClient(), new FakeSessionState("token-1"), new FakeTokenService(_ => new TokenResponseModel()),
            Options.Create(new ApiSettings { URL = "http://localhost", Version = "2" }), NullLogger<UiDataClient>.Instance);

    [Fact]
    public async Task Data_client_posts_search_requests_to_the_versioned_entity_route()
    {
        using var handler = new ClientHttpHandler(_ => ClientHttpHandler.Json(HttpStatusCode.OK, BaseResponseDataModel<List<JsonObject>>.Success([new JsonObject { ["id"] = 1 }], 1)));
        var client = DataClient(handler);

        var result = await client.GetItemsAsync("Category", UiDataClient.SearchRequest([], 1, 10, null, true), Ct);
        await client.GetByIdAsync("Category", "a/b?c", Ct);

        result.Data!.Single()["id"]!.GetValue<int>().Should().Be(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.Should().Be("/api/v2/Category/getitems");
        handler.Requests[0].Bearer.Should().Be("token-1");
        handler.Requests[0].Body.Should().Contain("\"sortReverse\":true");
        handler.Requests[1].Uri.Should().Be("/api/v2/Category/getbyid?id=a%2Fb%3Fc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../admin")]
    [InlineData("Category/../User")]
    [InlineData("1Category")]
    [InlineData("Category?x=1")]
    public async Task Data_client_rejects_invalid_entity_names(string entity)
    {
        using var handler = new ClientHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = DataClient(handler);

        await FluentActions.Awaiting(() => client.GetAllAsync(entity, Ct)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.DeleteAsync(entity, [], Ct)).Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }
}
