using System.Text.Json.Nodes;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Validation;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Ui;

public sealed class UiLayoutValidatorTests
{
    private static readonly UiLayoutValidator Validator = new(UiWidgetCatalog.CreateDefault());

    /// <summary>Validates a page whose body is <paramref name="body"/> (a JSON array).</summary>
    private static List<string> Errors(string body, string header = "\"key\": \"test\"") =>
        Validator.Validate($$"""{ {{header}}, "body": {{body}} }""").Errors;

    [Fact]
    public void Accepts_a_complete_layout_using_every_builtin_widget()
    {
        const string layout = """
            {
              "key": "orders.list_v1", "title": "Orders", "kind": "page", "version": 3,
              "menu": { "title": "Orders", "icon": "list", "order": 5 }, "roles": ["clerk"],
              "body": [
                { "widget": "heading", "id": "title", "text": "Hello {{ user.Name }}", "level": 1 },
                { "widget": "text", "style": "muted", "text": "Total: {{ data.total }}", "visibleWhen": "data.total > 0" },
                { "widget": "alert", "severity": "warning", "text": "Check" },
                { "widget": "badge", "color": "success", "text": "ok" },
                { "widget": "divider" },
                { "widget": "link", "text": "Back", "href": "/ui/home", "style": "button" },
                { "widget": "columns", "widths": ["2", 1], "children": [
                  { "widget": "section", "title": "{{ data.name }}", "children": [
                    { "widget": "fields", "items": [ { "label": "Amount", "value": "{{ data.amount }}", "format": "N2", "visibleWhen": "data.amount != null" } ] } ] },
                  { "widget": "list", "source": "data.items", "empty": "None", "children": [ { "widget": "text", "text": "{{ row.name }}" } ] } ] },
                { "widget": "record", "id": "order", "entity": "Order", "recordId": "{{ query.id }}", "notFound": "Missing", "children": [ { "widget": "text", "text": "{{ record.number }}" } ] },
                { "widget": "grid", "entity": "Order", "title": "Orders", "pageSize": 25, "autoLoad": false,
                  "columns": [ { "field": "number", "title": "No", "sortable": false, "template": "#{{ row.number }}" } ],
                  "search": [
                    { "field": "status", "type": "select", "options": ["new", { "value": "done", "text": "Done" }] },
                    { "field": "customerId", "type": "picker", "lookup": { "entity": "Customer", "valueField": "id", "textField": "name",
                      "columns": [ { "field": "name" } ], "search": [ { "field": "name" } ] } } ],
                  "filter": { "categoryId": "{{ query.id }}" },
                  "create": true, "edit": true, "delete": true, "formTitle": "Order",
                  "form": [ { "field": "amount", "type": "number", "required": true, "min": 0, "max": 1000.5 },
                            { "field": "note", "type": "textarea", "maxLength": 200 } ],
                  "rowActions": [ { "text": "Open", "icon": "search", "style": "primary", "navigate": "/ui/order?id={{ row.id }}" } ] }
              ]
            }
            """;

        Validator.Validate(layout).Errors.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_json_is_reported_as_an_error() =>
        Validator.Validate("{ \"key\": ").Errors.Should().ContainSingle(e => e.StartsWith("Invalid JSON:", StringComparison.Ordinal));

    [Fact]
    public void Unknown_widgets_and_missing_widget_names_are_rejected()
    {
        Errors("""[ { "widget": "marquee" } ]""").Should().Equal("body[0].widget: unknown widget 'marquee'.");
        Errors("""[ { "text": "x" } ]""").Should().Equal("body[0].widget: required.");
    }

    [Fact]
    public void Unknown_properties_are_rejected()
    {
        Errors("""[ { "widget": "heading", "text": "x", "colour": "red" } ]""").Should().Equal("body[0](heading).colour: unknown property.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [ { "field": "a", "onClick": "x" } ] } ]""")
            .Should().Equal("body[0](grid).columns[0].onClick: unknown property.");
    }

    [Fact]
    public void Required_properties_are_reported_with_their_path()
    {
        Errors("""[ { "widget": "heading" } ]""").Should().Equal("body[0](heading).text: required.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [ { "title": "x" } ] } ]""").Should().Equal("body[0](grid).columns[0].field: required.");
        Errors("""[ { "widget": "link", "text": "x", "href": null } ]""").Should().Equal("body[0](link).href: required.");
    }

    [Theory]
    [InlineData("""{ "widget": "heading", "text": 5 }""", "body[0](heading).text: must be a string.")]
    [InlineData("""{ "widget": "heading", "text": "x", "level": "2" }""", "body[0](heading).level: must be an integer.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "pageSize": 2.5 }""", "body[0](grid).pageSize: must be an integer.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "create": "yes" }""", "body[0](grid).create: must be true or false.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": {} }""", "body[0](grid).columns: must be an array.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [ "a" ] }""", "body[0](grid).columns[0]: must be an object.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "filter": [] }""", "body[0](grid).filter: must be an object.")]
    [InlineData("""{ "widget": "columns", "widths": [ true ], "children": [ { "widget": "divider" } ] }""", "body[0](columns).widths[0]: must be a string.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "form": [ { "field": "a", "min": "1" } ], "edit": false }""", "body[0](grid).form[0].min: must be a number.")]
    public void Property_types_are_checked(string node, string expected) =>
        Errors($"[ {node} ]").Should().Contain(expected);

    [Fact]
    public void Enum_values_must_be_listed()
    {
        Errors("""[ { "widget": "alert", "text": "x", "severity": "fatal" } ]""")
            .Should().Equal("body[0](alert).severity: must be one of info, success, warning, danger.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "a", "type": "color" } ] } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](grid).search[0].type: must be one of text, textarea", StringComparison.Ordinal));
    }

    [Fact]
    public void Expressions_and_templates_are_compiled()
    {
        Errors("""[ { "widget": "divider", "visibleWhen": "data.amount >" } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0].visibleWhen: ", StringComparison.Ordinal));
        Errors("""[ { "widget": "text", "text": "Total {{ data.amount + }}" } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](text).text: ", StringComparison.Ordinal));
        Errors("""[ { "widget": "list", "source": "System.IO.Directory", "children": [ { "widget": "divider" } ] } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](list).source: ", StringComparison.Ordinal));
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "filter": { "a": "{{ secret.value }}" } } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](grid).filter.a: ", StringComparison.Ordinal));
        Errors("""[ { "widget": "fields", "items": [ { "label": "a", "value": "x", "visibleWhen": "nope(" } ] } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](fields).items[0].visibleWhen: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("N0")]
    [InlineData("N6")]
    [InlineData("F0")]
    [InlineData("F6")]
    [InlineData("d")]
    [InlineData("D")]
    [InlineData("g")]
    [InlineData("t")]
    public void Supported_formats_are_accepted(string format)
    {
        UiFormats.IsValid(format).Should().BeTrue();
        Errors($$"""[ { "widget": "fields", "items": [ { "label": "a", "value": "x", "format": "{{format}}" } ] } ]""").Should().BeEmpty();
        Errors($$"""[ { "widget": "grid", "entity": "A", "columns": [ { "field": "a", "format": "{{format}}" } ] } ]""").Should().BeEmpty();
    }

    [Theory]
    [InlineData("N7")]
    [InlineData("C2")]
    [InlineData("n2")]
    [InlineData("yyyy-MM-dd")]
    [InlineData("")]
    public void Unsupported_formats_are_rejected(string format)
    {
        UiFormats.IsValid(format).Should().BeFalse();
        Errors($$"""[ { "widget": "fields", "items": [ { "label": "a", "value": "x", "format": "{{format}}" } ] } ]""")
            .Should().Equal("body[0](fields).items[0].format: unsupported format; use N0–N6, F0–F6, d, D, g or t.");
        Errors($$"""[ { "widget": "grid", "entity": "A", "columns": [ { "field": "a", "format": "{{format}}" } ] } ]""")
            .Should().Equal("body[0](grid).columns[0].format: unsupported format; use N0–N6, F0–F6, d, D, g or t.");
    }

    [Fact]
    public void Formats_must_be_strings() =>
        Errors("""[ { "widget": "fields", "items": [ { "label": "a", "value": "x", "format": 2 } ] } ]""")
            .Should().Equal("body[0](fields).items[0].format: must be a string.");

    [Theory]
    [InlineData("data.name.GetType()", "Unknown method")]
    [InlineData("data.name.Substring(1)", "Unknown method")]
    [InlineData("System.IO.File", "Unknown variable")]
    [InlineData("secret.value", "Unknown variable")]
    [InlineData("Math.Pow(2, 3)", "Unknown method")]
    [InlineData("new object()", "Unknown variable")]
    [InlineData("data.a = 1", "Unexpected")]
    public void Portable_expressions_reject_unknown_variables_and_methods(string expression, string message)
    {
        Errors($$"""[ { "widget": "divider", "visibleWhen": "{{expression}}" } ]""")
            .Should().ContainSingle().Which.Should().StartWith("body[0].visibleWhen: ").And.Contain(message);
        Errors($$$"""[ { "widget": "text", "text": "x {{ {{{expression}}} }} y" } ]""")
            .Should().ContainSingle().Which.Should().StartWith("body[0](text).text: ");
    }

    [Theory]
    [InlineData("user.IsInRole(\"admin\") && data.amount >= 10")]
    [InlineData("data.name.ToUpper().Contains(\"A\")")]
    [InlineData("Math.Round(data.amount * 1.18, 2) > 0")]
    [InlineData("data.items.Count > 0 || data.items.Length > 0")]
    [InlineData("(data.a ?? 0) % 2 == 1 ? true : false")]
    [InlineData("row.tags.Contains(\"x\") || record.id != null || instance.state == \"a\" || query.id == \"1\" || now != null")]
    public void Portable_expressions_accept_the_documented_language(string expression) =>
        Errors($$"""[ { "widget": "divider", "visibleWhen": {{System.Text.Json.JsonSerializer.Serialize(expression)}} } ]""").Should().BeEmpty();

    [Fact]
    public void Layouts_may_declare_their_json_schema_and_version_defaults_to_zero()
    {
        var layout = UiJson.Parse("""{ "$schema": "../../spec/ui-layout.schema.json", "key": "s", "body": [] }""");

        Validator.Validate(layout).IsValid.Should().BeTrue();
        layout.Version.Should().Be(0);
        layout.Schema.Should().Be("../../spec/ui-layout.schema.json");
        UiJson.Serialize(layout).Should().Contain("\"$schema\"");
    }

    [Fact]
    public void Children_are_only_allowed_where_the_widget_supports_them()
    {
        Errors("""[ { "widget": "divider", "children": [ { "widget": "divider" } ] } ]""")
            .Should().Equal("body[0](divider).children: 'divider' has no children.");
        Errors("""[ { "widget": "section", "title": "x" } ]""")
            .Should().Equal("body[0](section).children: 'section' needs at least one child.");
        Errors("""[ { "widget": "list", "source": "data.items", "children": [] } ]""")
            .Should().Equal("body[0](list).children: 'list' needs at least one child.");
    }

    [Fact]
    public void Children_are_validated_with_nested_paths()
    {
        Errors("""[ { "widget": "section", "children": [ { "widget": "divider" }, { "widget": "heading" } ] } ]""")
            .Should().Equal("body[0](section).children[1](heading).text: required.");
    }

    [Fact]
    public void Record_widget_takes_a_node_id_and_a_record_id_template()
    {
        Errors("""[ { "widget": "record", "id": "category", "entity": "Category", "recordId": "{{ query.id }}", "children": [ { "widget": "divider" } ] } ]""")
            .Should().BeEmpty();
        Errors("""[ { "widget": "record", "id": "category", "entity": "Category", "children": [ { "widget": "divider" } ] } ]""")
            .Should().Equal("body[0](record).recordId: required.");
        Errors("""[ { "widget": "record", "entity": "Category", "recordId": "{{ query.id + }}", "children": [ { "widget": "divider" } ] } ]""")
            .Should().ContainSingle(e => e.StartsWith("body[0](record).recordId: ", StringComparison.Ordinal));

        var node = UiJson.Parse("""{ "key": "r", "body": [ { "widget": "record", "id": "category", "entity": "Category", "recordId": "{{ query.id }}", "children": [ { "widget": "divider" } ] } ] }""").Body[0];
        node.Id.Should().Be("category");
        node.StringProp("recordId").Should().Be("{{ query.id }}");
    }

    [Fact]
    public void Non_object_items_in_field_lists_are_reported_instead_of_crashing()
    {
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [ "name" ], "search": [ 1 ] } ]""")
            .Should().BeEquivalentTo("body[0](grid).columns[0]: must be an object.", "body[0](grid).search[0]: must be an object.");
    }

    [Fact]
    public void Ids_must_be_unique_identifiers()
    {
        Errors("""[ { "widget": "divider", "id": "a" }, { "widget": "section", "children": [ { "widget": "divider", "id": "a" } ] } ]""")
            .Should().Equal("body[1](section).children[0].id: must be a unique identifier.");
        Errors("""[ { "widget": "divider", "id": "not valid" } ]""").Should().Equal("body[0].id: must be a unique identifier.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Orders")]
    [InlineData("-orders")]
    [InlineData("orders/list")]
    [InlineData("../etc")]
    [InlineData("a b")]
    public void Keys_must_be_lowercase_slugs(string key)
    {
        UiLayoutValidator.IsValidKey(key).Should().BeFalse();
        Errors("[]", $"\"key\": \"{key}\"").Should().ContainSingle(e => e.StartsWith("key: required", StringComparison.Ordinal));
    }

    [Fact]
    public void Key_length_is_limited()
    {
        UiLayoutValidator.IsValidKey(new string('a', 100)).Should().BeTrue();
        UiLayoutValidator.IsValidKey(new string('a', 101)).Should().BeFalse();
        UiLayoutValidator.IsValidKey(null).Should().BeFalse();
    }

    [Fact]
    public void Document_level_rules_are_enforced()
    {
        Errors("[]", "\"key\": \"v\", \"kind\": \"view\", \"menu\": { \"title\": \"x\" }").Should().Equal("menu: only pages can have a menu entry.");
        Errors("[]", "\"key\": \"v\", \"kind\": \"dialog\"").Should().Equal("kind: must be 'page' or 'view'.");
        Errors("[]", "\"key\": \"v\", \"version\": -1").Should().Equal("version: must not be negative.");
        Errors("[]", "\"key\": \"v\", \"roles\": [\"admin\", \" \"]").Should().Equal("roles: must not contain empty values.");
    }

    [Fact]
    public void Grid_with_create_or_edit_needs_a_form()
    {
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "create": true } ]""").Should().Equal("body[0](grid).form: required when create or edit is enabled.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "edit": true, "form": [] } ]""").Should().Equal("body[0](grid).form: required when create or edit is enabled.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "delete": true } ]""").Should().BeEmpty("delete needs no form");
    }

    [Fact]
    public void Picker_fields_need_a_lookup_and_select_fields_need_options_or_lookup()
    {
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker" } ] } ]""")
            .Should().Equal("body[0](grid).search[0].lookup: required for picker fields.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "edit": true, "form": [ { "field": "c", "type": "select" } ] } ]""")
            .Should().Equal("body[0](grid).form[0]: select fields need options or lookup.");
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "select", "lookup": { "entity": "C", "valueField": "id", "textField": "name" } } ] } ]""")
            .Should().BeEmpty();
    }

    [Fact]
    public void Lookups_require_entity_value_and_text_fields()
    {
        Errors("""[ { "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker", "lookup": { "entity": "C" } } ] } ]""")
            .Should().BeEquivalentTo("body[0](grid).search[0].lookup.valueField: required.", "body[0](grid).search[0].lookup.textField: required.");
    }

    [Theory]
    [InlineData("""{ "widget": "grid", "entity": "Order;drop", "columns": [] }""", "body[0](grid).entity: must be a controller name (letters and digits).")]
    [InlineData("""{ "widget": "grid", "entity": "../admin", "columns": [] }""", "body[0](grid).entity: must be a controller name (letters and digits).")]
    [InlineData("""{ "widget": "record", "entity": "1Order", "recordId": "1", "children": [ { "widget": "divider" } ] }""", "body[0](record).entity: must be a controller name (letters and digits).")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [ { "field": "a-b" } ] }""", "body[0](grid).columns[0].field: invalid field name 'a-b'.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "a.b" } ] }""", "body[0](grid).search[0].field: invalid field name 'a.b'.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "filter": { "x y": "1" } }""", "body[0](grid).filter.x y: invalid field name.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker", "lookup": { "entity": "C$", "valueField": "id", "textField": "n" } } ] }""", "body[0](grid).search[0].lookup.entity: must be a controller name (letters and digits).")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker", "lookup": { "entity": "C", "valueField": "a-b", "textField": "n" } } ] }""", "body[0](grid).search[0].lookup.valueField: invalid field name 'a-b'.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker", "lookup": { "entity": "C", "valueField": "id", "textField": "a.b" } } ] }""", "body[0](grid).search[0].lookup.textField: invalid field name 'a.b'.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "search": [ { "field": "c", "type": "picker", "lookup": { "entity": "C", "valueField": "id", "textField": "n", "columns": [ { "field": "x-y" } ] } } ] }""", "body[0](grid).search[0].lookup.columns[0].field: invalid field name 'x-y'.")]
    public void Entity_and_field_names_are_restricted(string node, string expected) =>
        Errors($"[ {node} ]").Should().Equal(expected);

    [Theory]
    [InlineData("""{ "widget": "heading", "text": "x", "level": 7 }""", "body[0](heading).level: must be between 1 and 6.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "pageSize": 0 }""", "body[0](grid).pageSize: must be between 1 and 500.")]
    [InlineData("""{ "widget": "grid", "entity": "A", "columns": [], "pageSize": 501 }""", "body[0](grid).pageSize: must be between 1 and 500.")]
    public void Numeric_ranges_are_checked(string node, string expected) =>
        Errors($"[ {node} ]").Should().Equal(expected);

    [Fact]
    public void Layouts_with_too_many_widgets_are_rejected()
    {
        var layout = new UiLayoutDocument { Key = "big", Body = [.. Enumerable.Range(0, UiLayoutValidator.MaxNodes + 1).Select(_ => new UiNode { Widget = "divider" })] };

        Validator.Validate(layout).Errors.Should().Equal($"Layout has more than {UiLayoutValidator.MaxNodes} widgets.");

        layout.Body.RemoveAt(0);
        Validator.Validate(layout).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Deeply_nested_layouts_are_rejected()
    {
        static UiNode Nest(int depth) => depth == 0
            ? new UiNode { Widget = "divider" }
            : new UiNode { Widget = "section", Children = [Nest(depth - 1)] };

        Validator.Validate(new UiLayoutDocument { Key = "ok", Body = [Nest(UiLayoutValidator.MaxDepth - 1)] }).IsValid.Should().BeTrue();
        Validator.Validate(new UiLayoutDocument { Key = "deep", Body = [Nest(UiLayoutValidator.MaxDepth)] }).Errors
            .Should().ContainSingle(e => e.EndsWith($"nesting is deeper than {UiLayoutValidator.MaxDepth} levels.", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_widgets_are_validated_with_their_spec_and_extra_rule()
    {
        var catalog = UiWidgetCatalog.CreateDefault().Add(new UiWidgetSpec("chart", "Chart")
        {
            Props = [new("series", UiPropType.Expression) { Required = true }, new("height", UiPropType.Integer)],
            Validate = (node, path, errors) =>
            {
                if (node.IntProp("height") > 800)
                {
                    errors.Add($"{path}.height: too tall.");
                }
            },
        });
        var validator = new UiLayoutValidator(catalog);

        validator.Validate("""{ "key": "c", "body": [ { "widget": "chart", "series": "data.points" } ] }""").IsValid.Should().BeTrue();
        validator.Validate("""{ "key": "c", "body": [ { "widget": "chart", "series": "data.points", "height": 900 } ] }""").Errors
            .Should().Equal("body[0](chart).height: too tall.");
        Validator.Validate("""{ "key": "c", "body": [ { "widget": "chart", "series": "data.points" } ] }""").IsValid
            .Should().BeFalse("the default catalog does not know the custom widget");
    }

    [Fact]
    public void UiJson_round_trips_widget_properties_and_omits_nulls()
    {
        var layout = UiJson.Parse("""{ "key": "k", "body": [ { "widget": "heading", "id": "h", "text": "Hi", "level": 3 } ], }""");

        layout.Body[0].Props!.Select(p => p.Key).Should().Equal("text", "level");
        layout.Body[0].StringProp("text").Should().Be("Hi");
        layout.Body[0].IntProp("level").Should().Be(3);
        layout.Body[0].BoolProp("missing", fallback: true).Should().BeTrue();
        layout.Kind.Should().Be(UiLayoutKind.Page);

        var serialized = UiJson.Serialize(layout);
        var json = JsonNode.Parse(serialized)!;
        json["body"]![0]!.ToJsonString().Should().Be("""{"widget":"heading","id":"h","text":"Hi","level":3}""");
        UiJson.Serialize(UiJson.Parse(serialized)).Should().Be(serialized);
        json["body"]![0]!.AsObject().ContainsKey("visibleWhen").Should().BeFalse();
        json.AsObject().ContainsKey("menu").Should().BeFalse();
    }

    [Fact]
    public void UiJson_round_trips_nested_children_and_null_props()
    {
        const string json = """{"key":"k","body":[{"widget":"section","visibleWhen":"data.a","title":"T","extra":null,"children":[{"widget":"list","source":"data.items","children":[{"widget":"text","text":"{{ row.name }}"}]}]}]}""";

        var layout = UiJson.Parse(json);
        var reparsed = UiJson.Parse(UiJson.Serialize(layout));

        reparsed.Body[0].VisibleWhen.Should().Be("data.a");
        reparsed.Body[0].Props!.ContainsKey("extra").Should().BeTrue();
        reparsed.Body[0].Children![0].Children![0].StringProp("text").Should().Be("{{ row.name }}");
        System.Text.Json.JsonSerializer.Serialize(reparsed.Body[0].Children![0], System.Text.Json.JsonSerializerOptions.Web)
            .Should().Be("""{"widget":"list","source":"data.items","children":[{"widget":"text","text":"{{ row.name }}"}]}""");
    }

    [Theory]
    [InlineData("""{ "key": "k", "body": [ "heading" ] }""")]
    [InlineData("""{ "key": "k", "body": [ { "widget": 5 } ] }""")]
    [InlineData("""{ "key": "k", "body": [ { "widget": "text", "visibleWhen": true } ] }""")]
    public void Malformed_nodes_are_json_errors(string json)
    {
        FluentActions.Invoking(() => UiJson.Parse(json)).Should().Throw<System.Text.Json.JsonException>();
        Validator.Validate(json).Errors.Should().ContainSingle(e => e.StartsWith("Invalid JSON:", StringComparison.Ordinal));
    }

    [Fact]
    public void Widget_spec_models_describe_props_for_editors()
    {
        var grid = UiWidgetSpecModel.From(UiWidgets.Grid);
        var section = UiWidgetSpecModel.From(UiWidgets.Section);

        grid.Interactive.Should().BeTrue();
        grid.Children.Should().Be("none");
        grid.Props.Single(p => p.Name == "filter").Type.Should().Be("templateMap");
        grid.Props.Single(p => p.Name == "entity").Required.Should().BeTrue();
        grid.Props.Single(p => p.Name == "columns").Properties!.Select(p => p.Name).Should().Contain("field");
        section.Children.Should().Be("required");
        UiWidgetSpecModel.From(UiWidgets.Alert).Props.Single(p => p.Name == "severity").Values.Should().Equal("info", "success", "warning", "danger");
    }
}
