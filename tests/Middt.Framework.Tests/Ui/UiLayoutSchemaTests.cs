using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Ui.Schema;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Ui;

public sealed class UiLayoutSchemaTests
{
    private static JsonObject Schema(UiWidgetCatalog? catalog = null) =>
        JsonNode.Parse(UiLayoutSchema.GenerateJson(catalog ?? UiWidgetCatalog.CreateDefault()))!.AsObject();

    private static JsonObject Def(JsonObject schema, string widget) => schema["$defs"]![$"widget-{widget}"]!.AsObject();

    private static List<string?> Strings(JsonNode? array) => [.. array!.AsArray().Select(n => n?.GetValue<string>())];

    internal static string SchemaFile => Path.Combine(UiSampleLayoutTests.RepositoryRoot(), "spec", "ui-layout.schema.json");

    [Fact]
    public void Generated_schema_is_valid_json_with_the_document_properties()
    {
        var json = UiLayoutSchema.GenerateJson(UiWidgetCatalog.CreateDefault());
        var schema = Schema();

        FluentActions.Invoking(() => JsonDocument.Parse(json).Dispose()).Should().NotThrow();
        json.Should().EndWith("}\n");
        schema["$schema"]!.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
        schema["$id"]!.GetValue<string>().Should().Be(UiLayoutSchema.Id);
        Strings(schema["required"]).Should().Equal("key", "body");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["properties"]!.AsObject().Select(p => p.Key).Should().Contain(["$schema", "key", "title", "kind", "version", "menu", "roles", "body"]);
        Strings(schema["properties"]!["kind"]!["enum"]).Should().Equal("page", "view");
        schema["properties"]!["body"]!["$ref"]!.GetValue<string>().Should().Be("#/$defs/nodes");
    }

    [Fact]
    public void Every_widget_has_a_definition_and_the_node_dispatches_on_the_widget_name()
    {
        var schema = Schema();
        var names = UiWidgets.All.Select(w => w.Name).Order(StringComparer.Ordinal).ToList();

        foreach (var name in names)
        {
            var def = Def(schema, name);
            def["properties"]!["widget"]!["const"]!.GetValue<string>().Should().Be(name);
            def["additionalProperties"]!.GetValue<bool>().Should().BeFalse(name);
        }

        var node = schema["$defs"]!["node"]!;
        Strings(node["properties"]!["widget"]!["enum"]).Should().Equal(names);
        node["allOf"]!.AsArray().Select(c => c!["then"]!["$ref"]!.GetValue<string>()).Should().Equal(names.Select(n => $"#/$defs/widget-{n}"));
    }

    [Fact]
    public void Required_props_and_children_are_listed()
    {
        var schema = Schema();

        Strings(Def(schema, "heading")["required"]).Should().Equal("widget", "text");
        Strings(Def(schema, "section")["required"]).Should().Equal("widget", "children");
        Strings(Def(schema, "divider")["required"]).Should().Equal("widget");
        Def(schema, "divider")["properties"]!.AsObject().ContainsKey("children").Should().BeFalse();
        Strings(Def(schema, "grid")["required"]).Should().Contain(["widget", "entity", "columns"]);
        Strings(Def(schema, "grid")["properties"]!["columns"]!["items"]!["required"]).Should().Equal("field");
        Def(schema, "grid")["properties"]!["columns"]!["items"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        Def(schema, "grid")["description"]!.GetValue<string>().Should().EndWith("(interactive)");
    }

    [Fact]
    public void Enum_format_and_expression_props_are_described()
    {
        var schema = Schema();
        string[] formats = ["N0", "N1", "N2", "N3", "N4", "N5", "N6", "F0", "F1", "F2", "F3", "F4", "F5", "F6", "d", "D", "g", "t"];

        Strings(Def(schema, "alert")["properties"]!["severity"]!["enum"]).Should().Equal("info", "success", "warning", "danger");
        Strings(Def(schema, "fields")["properties"]!["items"]!["items"]!["properties"]!["format"]!["enum"]).Should().Equal(formats);
        Strings(Def(schema, "grid")["properties"]!["columns"]!["items"]!["properties"]!["format"]!["enum"]).Should().Equal(formats);
        Def(schema, "heading")["properties"]!["text"]!["x-middt"]!.GetValue<string>().Should().Be("template");
        Def(schema, "list")["properties"]!["source"]!["x-middt"]!.GetValue<string>().Should().Be("expression");
        Def(schema, "heading")["properties"]!["level"]!["type"]!.GetValue<string>().Should().Be("integer");
        Def(schema, "heading")["properties"]!["visibleWhen"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void Custom_widgets_are_included()
    {
        var catalog = UiWidgetCatalog.CreateDefault().Add(new UiWidgetSpec("chart", "Chart")
        {
            Props = [new("series", UiPropType.Expression) { Required = true }, new("kind", UiPropType.Enum) { Values = ["bar", "line"] }],
        });

        var chart = Def(Schema(catalog), "chart");

        Strings(chart["required"]).Should().Equal("widget", "series");
        Strings(chart["properties"]!["kind"]!["enum"]).Should().Equal("bar", "line");
    }

    [Theory]
    [MemberData(nameof(UiSampleLayoutTests.LayoutFileNames), MemberType = typeof(UiSampleLayoutTests))]
    public void Sample_layouts_only_use_properties_declared_in_the_schema(string file)
    {
        var schema = Schema();
        var layout = JsonNode.Parse(File.ReadAllText(Path.Combine(UiSampleLayoutTests.SampleDirectory("Layouts"), file)))!.AsObject();

        layout.Select(p => p.Key).Should().BeSubsetOf(schema["properties"]!.AsObject().Select(p => p.Key));
        Check(layout["body"]!.AsArray());

        void Check(JsonArray nodes)
        {
            foreach (var node in nodes.Select(n => n!.AsObject()))
            {
                var def = Def(schema, node["widget"]!.GetValue<string>());
                node.Select(p => p.Key).Should().BeSubsetOf(def["properties"]!.AsObject().Select(p => p.Key), file);
                Strings(def["required"]).Should().BeSubsetOf(node.Select(p => (string?)p.Key), file);
                if (node["children"] is JsonArray children)
                {
                    Check(children);
                }
            }
        }
    }

    /// <summary>Regenerate with <c>MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests</c>.</summary>
    [Fact]
    public void Schema_file_is_up_to_date()
    {
        var expected = UiLayoutSchema.GenerateJson(UiWidgetCatalog.CreateDefault());
        if (Environment.GetEnvironmentVariable("MIDDT_UPDATE_SPEC") == "1")
        {
            File.WriteAllText(SchemaFile, expected);
        }

        File.Exists(SchemaFile).Should().BeTrue("spec/ui-layout.schema.json is generated by this test with MIDDT_UPDATE_SPEC=1");
        File.ReadAllText(SchemaFile).ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"), "regenerate spec/ui-layout.schema.json with MIDDT_UPDATE_SPEC=1");
    }
}
