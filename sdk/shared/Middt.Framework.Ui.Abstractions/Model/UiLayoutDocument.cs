using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Middt.Framework.Ui.Model;

/// <summary>Layout kinds: a routable page (<c>/ui/{key}</c>) or a view embedded elsewhere (e.g. a workflow state).</summary>
public static class UiLayoutKind
{
    public const string Page = "page";
    public const string View = "view";
}

/// <summary>
/// A server driven screen: a tree of <see cref="UiNode"/>s (registered widgets) stored as JSON and editable at runtime.
/// <code>
/// { "key": "bike-category", "title": "Categories", "kind": "page", "menu": { "title": "Category", "icon": "category" },
///   "body": [ { "widget": "heading", "text": "Categories" }, { "widget": "grid", "entity": "Category", "columns": [ ... ] } ] }
/// </code>
/// </summary>
public sealed class UiLayoutDocument
{
    /// <summary>Optional JSON Schema reference for editors (<c>GET api/ui/schema</c>, <c>spec/ui-layout.schema.json</c>); ignored at runtime.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>Unique key (<c>a-z 0-9 - _ .</c>), also the page route: <c>/ui/{key}</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary><see cref="UiLayoutKind.Page"/> (default) or <see cref="UiLayoutKind.View"/>.</summary>
    public string Kind { get; set; } = UiLayoutKind.Page;

    /// <summary>Revision number; increases on every save. On PUT it is the version the change is based on (0 = new layout);
    /// seed files overwrite stored layouts only with a higher version (missing = 1).</summary>
    public int Version { get; set; }

    /// <summary>Menu entry of a page (omit for pages reached by links only).</summary>
    public UiMenu? Menu { get; set; }

    /// <summary>Roles allowed to open the layout (any of them); empty = every authenticated user.</summary>
    public List<string> Roles { get; set; } = [];

    public List<UiNode> Body { get; set; } = [];
}

public sealed class UiMenu
{
    public string? Title { get; set; }

    /// <summary>Material icon name.</summary>
    public string? Icon { get; set; }

    public int Order { get; set; }
}

/// <summary>
/// One widget instance: <c>{ "widget": "heading", "visibleWhen": "data.amount &gt; 0", "text": "Hello {{ user.Name }}" }</c>.
/// Widget specific properties are kept in <see cref="Props"/>; containers have <see cref="Children"/>.
/// </summary>
[JsonConverter(typeof(UiNodeJsonConverter))]
public sealed class UiNode
{
    /// <summary>Registered widget name.</summary>
    public string Widget { get; set; } = string.Empty;

    /// <summary>Optional id (unique per layout), used for element ids.</summary>
    public string? Id { get; set; }

    /// <summary>Boolean expression; the node is hidden when it evaluates to false.</summary>
    public string? VisibleWhen { get; set; }

    public List<UiNode>? Children { get; set; }

    /// <summary>Widget properties (everything except widget/id/visibleWhen/children).</summary>
    public JsonObject? Props { get; set; }

    public JsonNode? Prop(string name) => Props?[name];

    public string? StringProp(string name) =>
        Props?[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    public bool BoolProp(string name, bool fallback = false) =>
        Props?[name] is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? value.GetValue<bool>() : fallback;

    public int IntProp(string name, int fallback = 0) =>
        Props?[name] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : fallback;

    public JsonArray? ArrayProp(string name) => Props?[name] as JsonArray;

    public JsonObject? ObjectProp(string name) => Props?[name] as JsonObject;
}

/// <summary>
/// Reads/writes <see cref="UiNode"/> with its widget properties inline. (<c>[JsonExtensionData]</c> on a <see cref="JsonObject"/>
/// property writes the properties as a nested, invalid object on .NET 10, so the node is (de)serialized by hand.)
/// </summary>
internal sealed class UiNodeJsonConverter : JsonConverter<UiNode>
{
    private const string WidgetName = "widget";
    private const string IdName = "id";
    private const string VisibleWhenName = "visibleWhen";
    private const string ChildrenName = "children";

    public override UiNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject || JsonNode.Parse(ref reader) is not JsonObject json)
        {
            throw new JsonException("A widget must be a JSON object.");
        }

        var node = new UiNode();
        JsonObject? props = null;
        foreach (var (name, value) in json)
        {
            switch (name)
            {
                case WidgetName:
                    node.Widget = StringValue(value, name) ?? string.Empty;
                    break;
                case IdName:
                    node.Id = StringValue(value, name);
                    break;
                case VisibleWhenName:
                    node.VisibleWhen = StringValue(value, name);
                    break;
                case ChildrenName:
                    node.Children = value?.Deserialize<List<UiNode>>(options);
                    break;
                default:
                    (props ??= [])[name] = value?.DeepClone();
                    break;
            }
        }

        node.Props = props;
        return node;
    }

    public override void Write(Utf8JsonWriter writer, UiNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(WidgetName, value.Widget);
        if (value.Id is not null)
        {
            writer.WriteString(IdName, value.Id);
        }

        if (value.VisibleWhen is not null)
        {
            writer.WriteString(VisibleWhenName, value.VisibleWhen);
        }

        foreach (var (name, prop) in value.Props ?? [])
        {
            if (name is WidgetName or IdName or VisibleWhenName or ChildrenName)
            {
                continue;
            }

            writer.WritePropertyName(name);
            if (prop is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                prop.WriteTo(writer, options);
            }
        }

        if (value.Children is not null)
        {
            writer.WritePropertyName(ChildrenName);
            JsonSerializer.Serialize(writer, value.Children, options);
        }

        writer.WriteEndObject();
    }

    private static string? StringValue(JsonNode? value, string name) => value switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => throw new JsonException($"'{name}' must be a string."),
    };
}

/// <summary>List entry of a stored layout.</summary>
public sealed class UiLayoutSummary
{
    public string Key { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Kind { get; set; } = UiLayoutKind.Page;

    public int Version { get; set; }

    public UiMenu? Menu { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

/// <summary>A previous version of a layout.</summary>
public sealed class UiLayoutRevisionModel
{
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public string Content { get; set; } = string.Empty;
}

/// <summary>Result of a validation: empty <see cref="Errors"/> means valid.</summary>
public sealed class UiValidationResult
{
    public List<string> Errors { get; set; } = [];

    public bool IsValid => Errors.Count == 0;
}

public static class UiJson
{
    /// <summary>Serializer options of layout documents (camelCase, nulls omitted, indented).</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    public static UiLayoutDocument Parse(string json) =>
        JsonSerializer.Deserialize<UiLayoutDocument>(json, Options) ?? throw new JsonException("Layout is empty.");

    public static string Serialize(UiLayoutDocument document) => JsonSerializer.Serialize(document, Options);
}

/// <summary>Widget description for editors (<c>GET api/ui/widgets</c>).</summary>
public sealed class UiWidgetSpecModel
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary><c>none</c>, <c>optional</c> or <c>required</c>.</summary>
    public string Children { get; set; } = "none";

    public bool Interactive { get; set; }

    public List<UiPropSpecModel> Props { get; set; } = [];

    public static UiWidgetSpecModel From(Widgets.UiWidgetSpec spec) => new()
    {
        Name = spec.Name,
        Description = spec.Description,
        Children = spec.Children.ToString().ToLowerInvariant(),
        Interactive = spec.Interactive,
        Props = [.. spec.Props.Select(UiPropSpecModel.From)],
    };
}

public sealed class UiPropSpecModel
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Property type in camelCase (string, template, expression, number, integer, boolean, enum, object, array, templateMap, options).</summary>
    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Required { get; set; }

    public List<string>? Values { get; set; }

    public List<UiPropSpecModel>? Properties { get; set; }

    public static UiPropSpecModel From(Widgets.UiPropSpec spec) => new()
    {
        Name = spec.Name,
        Type = JsonNamingPolicy.CamelCase.ConvertName(spec.Type.ToString()),
        Description = spec.Description,
        Required = spec.Required,
        Values = spec.Values.Count > 0 ? [.. spec.Values] : null,
        Properties = spec.Properties.Count > 0 ? [.. spec.Properties.Select(From)] : null,
    };
}
