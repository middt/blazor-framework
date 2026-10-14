using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Middt.Framework.Ui.Model;

/// <summary>Grid column parsed from a layout (<c>{ field, title, format, width, sortable, template }</c>).</summary>
public sealed class UiColumn
{
    public string Field { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Format { get; init; }

    public string? Width { get; init; }

    public bool Sortable { get; init; } = true;

    public string? Template { get; init; }

    /// <summary>Property name sent to the API for sorting (PascalCase of <see cref="Field"/>).</summary>
    public string SortProperty => Field.Length == 0 ? Field : char.ToUpperInvariant(Field[0]) + Field[1..];

    public static List<UiColumn> ParseAll(JsonArray? columns) =>
        [.. (columns ?? []).OfType<JsonObject>().Select(c => new UiColumn
        {
            Field = UiFieldJson.GetString(c, "field") ?? string.Empty,
            Title = UiFieldJson.GetString(c, "title") ?? UiFieldJson.GetString(c, "field") ?? string.Empty,
            Format = UiFieldJson.GetString(c, "format"),
            Width = UiFieldJson.GetString(c, "width"),
            Sortable = UiFieldJson.GetBool(c, "sortable") ?? true,
            Template = UiFieldJson.GetString(c, "template"),
        })];
}

public sealed record UiOption(JsonNode? Value, string Text)
{
    /// <summary>Stable string key of <see cref="Value"/> (dropdown value).</summary>
    public string Key => UiFieldJson.Key(Value);
}

/// <summary>Options loaded from an entity (select) or picked in a dialog (picker).</summary>
public sealed class UiLookup
{
    public string Entity { get; init; } = string.Empty;

    public string ValueField { get; init; } = string.Empty;

    public string TextField { get; init; } = string.Empty;

    public string? Title { get; init; }

    public List<UiColumn> Columns { get; init; } = [];

    public List<UiField> Search { get; init; } = [];
}

/// <summary>Form/search field parsed from a layout.</summary>
public sealed class UiField
{
    public string Field { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>text, textarea, number, integer, date, datetime, checkbox, select or picker.</summary>
    public string Type { get; init; } = "text";

    public bool Required { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    public int? MaxLength { get; init; }

    public string? Placeholder { get; init; }

    public List<UiOption> Options { get; init; } = [];

    public UiLookup? Lookup { get; init; }

    public static List<UiField> ParseAll(JsonArray? fields) => [.. (fields ?? []).OfType<JsonObject>().Select(Parse)];

    public static UiField Parse(JsonObject f) => new()
    {
        Field = UiFieldJson.GetString(f, "field") ?? string.Empty,
        Label = UiFieldJson.GetString(f, "label") ?? UiFieldJson.GetString(f, "field") ?? string.Empty,
        Type = UiFieldJson.GetString(f, "type") ?? "text",
        Required = UiFieldJson.GetBool(f, "required") ?? false,
        Min = UiFieldJson.GetDecimal(f, "min"),
        Max = UiFieldJson.GetDecimal(f, "max"),
        MaxLength = UiFieldJson.GetDecimal(f, "maxLength") is { } maxLength ? (int)maxLength : null,
        Placeholder = UiFieldJson.GetString(f, "placeholder"),
        Options = [.. (f["options"] as JsonArray ?? []).Select(o => o switch
        {
            JsonObject obj => new UiOption(obj["value"]?.DeepClone(), UiFieldJson.GetString(obj, "text") ?? UiFieldJson.Key(obj["value"])),
            _ => new UiOption(o?.DeepClone(), UiFieldJson.Key(o)),
        })],
        Lookup = f["lookup"] is JsonObject lookup
            ? new UiLookup
            {
                Entity = UiFieldJson.GetString(lookup, "entity") ?? string.Empty,
                ValueField = UiFieldJson.GetString(lookup, "valueField") ?? string.Empty,
                TextField = UiFieldJson.GetString(lookup, "textField") ?? string.Empty,
                Title = UiFieldJson.GetString(lookup, "title"),
                Columns = UiColumn.ParseAll(lookup["columns"] as JsonArray),
                Search = ParseAll(lookup["search"] as JsonArray),
            }
            : null,
    };

    /// <summary>Validates the value of this field in <paramref name="target"/>; returns the error or <c>null</c>.</summary>
    public string? Validate(JsonObject target)
    {
        var value = target[Field];
        var empty = value is null || (value is JsonValue v && v.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetValue<string>()));
        if (empty)
        {
            return Required && Type != "checkbox" ? "Bu alan zorunludur." : null;
        }

        if (Type is "number" or "integer" && UiFieldJson.ToDecimal(value) is { } number)
        {
            if (Type == "integer" && decimal.Truncate(number) != number)
            {
                return "Tam sayı olmalıdır.";
            }

            if (Min is { } min && number < min)
            {
                return $"En az {min.ToString(CultureInfo.CurrentCulture)} olmalıdır.";
            }

            if (Max is { } max && number > max)
            {
                return $"En fazla {max.ToString(CultureInfo.CurrentCulture)} olabilir.";
            }
        }

        if (Type is "text" or "textarea" && MaxLength is { } maxLength && value is JsonValue s && s.TryGetValue<string>(out var text) && text.Length > maxLength)
        {
            return $"En fazla {maxLength} karakter olabilir.";
        }

        return null;
    }
}

/// <summary>Helpers to read layout JSON values.</summary>
public static class UiFieldJson
{
    public static string? GetString(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public static bool? GetBool(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

    public static decimal? GetDecimal(JsonObject obj, string name) => ToDecimal(obj[name]);

    public static decimal? ToDecimal(JsonNode? node) => node switch
    {
        // values created in code (JsonValue<long>, JsonValue<double>) do not convert with TryGetValue<decimal>: parse the JSON text
        JsonValue v when v.GetValueKind() == JsonValueKind.Number =>
            v.TryGetValue<decimal>(out var d) ? d
            : decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String && decimal.TryParse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static string Key(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString(),
    };
}
