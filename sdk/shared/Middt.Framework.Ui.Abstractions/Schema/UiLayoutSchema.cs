using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Schema;

/// <summary>
/// Generates the JSON Schema (draft 2020-12) of layout documents from a widget catalog: editors get completion and
/// validation, and TypeScript types can be generated for other frontends. Expressions and templates are plain strings in
/// the schema; their syntax is checked by <see cref="Validation.UiLayoutValidator"/>.
/// </summary>
public static class UiLayoutSchema
{
    public const string Id = "https://middt.dev/schemas/ui-layout.json";

    public static JsonObject Generate(UiWidgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var specs = catalog.Specs.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

        var defs = new JsonObject
        {
            ["nodes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/node" } },
            ["node"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("widget"),
                ["properties"] = new JsonObject
                {
                    ["widget"] = new JsonObject { ["enum"] = new JsonArray([.. specs.Select(s => (JsonNode?)s.Name)]) },
                },
                ["allOf"] = new JsonArray([.. specs.Select(s => (JsonNode?)new JsonObject
                {
                    ["if"] = new JsonObject { ["properties"] = new JsonObject { ["widget"] = new JsonObject { ["const"] = s.Name } } },
                    ["then"] = new JsonObject { ["$ref"] = $"#/$defs/widget-{s.Name}" },
                })]),
            },
        };

        foreach (var spec in specs)
        {
            var properties = new JsonObject
            {
                ["widget"] = new JsonObject { ["const"] = spec.Name, ["description"] = spec.Description },
                ["id"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Za-z_][A-Za-z0-9_]{0,63}$", ["description"] = "Unique id within the layout." },
                ["visibleWhen"] = new JsonObject { ["type"] = "string", ["description"] = "Portable expression; the widget is hidden when it is false." },
            };
            if (spec.Children != UiChildren.None)
            {
                properties["children"] = new JsonObject { ["$ref"] = "#/$defs/nodes" };
            }

            foreach (var prop in spec.Props)
            {
                properties[prop.Name] = Property(prop);
            }

            var required = new JsonArray("widget");
            foreach (var prop in spec.Props.Where(p => p.Required))
            {
                required.Add(prop.Name);
            }

            if (spec.Children == UiChildren.Required)
            {
                required.Add("children");
            }

            defs[$"widget-{spec.Name}"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = spec.Interactive ? $"{spec.Description} (interactive)" : spec.Description,
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false,
            };
        }

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = Id,
            ["title"] = "Middt UI layout",
            ["type"] = "object",
            ["required"] = new JsonArray("key", "body"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$schema"] = new JsonObject { ["type"] = "string" },
                ["key"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[a-z0-9][a-z0-9._-]{0,99}$", ["description"] = "Unique key, also the page route /ui/{key}." },
                ["title"] = new JsonObject { ["type"] = "string" },
                ["description"] = new JsonObject { ["type"] = "string" },
                ["kind"] = new JsonObject { ["enum"] = new JsonArray("page", "view"), ["default"] = "page" },
                ["version"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["description"] = "Seed files: higher versions replace stored layouts. PUT: the version the change is based on (0 = new)." },
                ["menu"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["icon"] = new JsonObject { ["type"] = "string", ["description"] = "Material icon name." },
                        ["order"] = new JsonObject { ["type"] = "integer" },
                    },
                },
                ["roles"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["body"] = new JsonObject { ["$ref"] = "#/$defs/nodes" },
            },
            ["$defs"] = defs,
        };
    }

    public static string GenerateJson(UiWidgetCatalog catalog) =>
        Generate(catalog).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

    private static JsonObject Property(UiPropSpec prop)
    {
        var schema = prop.Type switch
        {
            UiPropType.String => new JsonObject { ["type"] = "string" },
            UiPropType.Template => new JsonObject { ["type"] = "string", ["x-middt"] = "template" },
            UiPropType.Expression => new JsonObject { ["type"] = "string", ["x-middt"] = "expression" },
            UiPropType.Number => new JsonObject { ["type"] = "number" },
            UiPropType.Integer => new JsonObject { ["type"] = "integer" },
            UiPropType.Boolean => new JsonObject { ["type"] = "boolean" },
            UiPropType.Enum => new JsonObject { ["enum"] = new JsonArray([.. prop.Values.Select(v => (JsonNode?)v)]) },
            UiPropType.Format => new JsonObject { ["enum"] = new JsonArray([.. FormatValues().Select(v => (JsonNode?)v)]) },
            UiPropType.Object => ObjectSchema(prop.Properties),
            UiPropType.Array => new JsonObject { ["type"] = "array", ["items"] = ObjectSchema(prop.Properties) },
            UiPropType.TemplateMap => new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean", "null") },
            },
            UiPropType.Options => new JsonObject
            {
                ["type"] = "array",
                ["items"] = prop.Properties.Count > 0
                    ? new JsonObject { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = new JsonArray("string", "number") }, ObjectSchema(prop.Properties)) }
                    : new JsonObject { ["type"] = new JsonArray("string", "number") },
            },
            _ => new JsonObject(),
        };

        if (prop.Description is { } description)
        {
            schema["description"] = description;
        }

        return schema;
    }

    private static JsonObject ObjectSchema(IReadOnlyList<UiPropSpec> properties)
    {
        var props = new JsonObject();
        foreach (var p in properties)
        {
            props[p.Name] = Property(p);
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
        if (properties.Any(p => p.Required))
        {
            schema["required"] = new JsonArray([.. properties.Where(p => p.Required).Select(p => (JsonNode?)p.Name)]);
        }

        return schema;
    }

    private static IEnumerable<string> FormatValues() =>
        [.. Enumerable.Range(0, 7).Select(i => $"N{i}"), .. Enumerable.Range(0, 7).Select(i => $"F{i}"), .. UiFormats.Dates];
}
