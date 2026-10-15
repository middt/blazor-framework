using System.Text.Json.Nodes;
using Middt.Framework.Blazor.Web.Base.Component.Workflow;

namespace Middt.Framework.Tests.Blazor;

/// <summary>
/// Start dialog prefill from the URL (<c>/workflow/x?start=1&amp;key=…&amp;field=…</c>): query strings converted to typed
/// JSON by the start input schema (<see cref="WorkflowUi.StartPrefill"/>).
/// </summary>
public sealed class WorkflowStartPrefillTests
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "categoryId": { "type": "integer", "title": "Kategori", "x-lookup": { "entity": "Category", "valueField": "categoryId", "textField": "categoryName" } },
            "storeCode": { "type": "string", "x-lookup": { "entity": "Store", "valueField": "code", "textField": "name" } },
            "price": { "type": "number" },
            "active": { "type": "boolean" },
            "name": { "type": "string" },
            "quantity": { "type": ["integer", "null"] },
            "key": { "type": "string" },
            "start": { "type": "string" },
            "tags": { "type": "array" }
          }
        }
        """;

    private static JsonObject ParseSchema() => JsonNode.Parse(Schema)!.AsObject();

    private static Dictionary<string, string?> Query(params (string Name, string? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Query_values_are_converted_by_the_property_type()
    {
        var prefill = WorkflowUi.StartPrefill(ParseSchema(), Query(
            ("categoryId", "5"), ("storeCode", "IST"), ("price", "4.5"), ("active", "true"), ("name", "Yol"), ("quantity", "3")));

        prefill!.ToJsonString().Should().Be("""{"categoryId":5,"storeCode":"IST","price":4.5,"active":true,"name":"Yol","quantity":3}""");
    }

    [Fact]
    public void Start_key_unknown_and_unconvertible_values_are_ignored()
    {
        var query = Query(("start", "1"), ("key", "category-5"), ("other", "x"), ("categoryId", "abc"), ("price", "5"), ("tags", "[1]"));

        WorkflowUi.StartPrefill(ParseSchema(), query)!.ToJsonString().Should().Be("""{"price":5}""");

        // matched names (removed from the URL) include values that did not convert, never start/key/unknown names
        WorkflowUi.StartPrefillParameters(ParseSchema(), query.Keys).Should().Equal("categoryId", "price", "tags");
    }

    [Fact]
    public void Nothing_applies_without_schema_properties_or_values()
    {
        WorkflowUi.StartPrefill(null, Query(("categoryId", "5"))).Should().BeNull();
        WorkflowUi.StartPrefill(ParseSchema(), null).Should().BeNull();
        WorkflowUi.StartPrefill(ParseSchema(), Query(("other", "5"))).Should().BeNull();
        WorkflowUi.StartPrefillParameters(JsonNode.Parse("""{ "type": "object" }""")!.AsObject(), ["categoryId"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("42", "integer", "42")]
    [InlineData("-7", "integer", "-7")]
    [InlineData("4.5", "integer", null)]
    [InlineData("4.5", "number", "4.5")]
    [InlineData("1e3", "number", "1000")]
    [InlineData("x", "number", null)]
    [InlineData("TRUE", "boolean", "true")]
    [InlineData("0", "boolean", "false")]
    [InlineData("yes", "boolean", null)]
    [InlineData("42", "string", "\"42\"")]
    [InlineData("2026-01-02", null, "\"2026-01-02\"")]
    [InlineData("{}", "object", null)]
    [InlineData("", "string", null)]
    public void Query_values_follow_the_schema_type(string text, string? type, string? json) =>
        (WorkflowUi.QueryValue(text, type)?.ToJsonString()).Should().Be(json);

    [Fact]
    public void Type_arrays_use_the_first_non_null_type() =>
        WorkflowUi.SchemaType(JsonNode.Parse("""{ "type": ["null", "number"] }""")!.AsObject()).Should().Be("number");
}
