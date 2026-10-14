using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Component.Workflow;

/// <summary>
/// Renders an input form for a JSON Schema subset (<c>type: object</c> with flat <c>properties</c>):
/// string (text, long text, enum, date/date-time, e-mail), number/integer and boolean; nested objects/arrays fall back
/// to a JSON text area. The value is exposed as a <see cref="JsonObject"/>; call <see cref="Validate"/> before
/// submitting to show required/length/range errors next to the fields.
/// </summary>
public partial class JsonSchemaForm : ComponentBase
{
    private const int LongTextThreshold = 200;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant, PatternTimeout);

    private readonly List<SchemaField> fields = [];
    private readonly Dictionary<string, IRadzenFormComponent> inputs = new(StringComparer.Ordinal);
    private JsonObject? renderedSchema;
    private bool validated;

    /// <summary>JSON Schema of the object to edit.</summary>
    [Parameter]
    public JsonObject? Schema { get; set; }

    /// <summary>Initial value (read when the schema changes) and the current value via <see cref="ValueChanged"/>.</summary>
    [Parameter]
    public JsonObject? Value { get; set; }

    [Parameter]
    public EventCallback<JsonObject> ValueChanged { get; set; }

    /// <summary>Prefix for the element ids (unique per page when several forms are shown).</summary>
    [Parameter]
    public string IdPrefix { get; set; } = "jsf";

    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Text shown when the schema has no properties.</summary>
    [Parameter]
    public string? EmptyText { get; set; }

    /// <summary><c>true</c> when the schema declares at least one property.</summary>
    public bool HasFields => fields.Count > 0;

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Schema, renderedSchema))
        {
            renderedSchema = Schema;
            validated = false;
            BuildFields(Schema, Value);
        }
    }

    /// <summary>Validates all fields, shows the errors and returns <c>true</c> when the value is valid.</summary>
    public bool Validate()
    {
        validated = true;
        var valid = true;
        foreach (var field in fields)
        {
            field.Error = ValidateField(field);
            valid &= field.Error is null;
        }

        StateHasChanged();
        return valid;
    }

    /// <summary>Builds the current value (empty optional fields are omitted).</summary>
    public JsonObject GetValue()
    {
        var result = new JsonObject();
        foreach (var field in fields)
        {
            if (ToJson(field) is { } node)
            {
                result[field.Name] = node;
            }
        }

        return result;
    }

    /// <summary>Moves the keyboard focus to the first field (e.g. when a wizard step form is shown).</summary>
    public async Task FocusFirstAsync()
    {
        if (fields.Count > 0 && inputs.TryGetValue(fields[0].Name, out var input))
        {
            try
            {
                await input.FocusAsync();
            }
            catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or InvalidOperationException or TaskCanceledException)
            {
                // focus is a convenience; ignore when the element is gone or JS is unavailable (prerendering)
            }
        }
    }

    /// <summary>Clears the entered values and errors.</summary>
    public void Reset()
    {
        validated = false;
        BuildFields(Schema, null);
        StateHasChanged();
    }

    private static Dictionary<string, object> AriaLabel(SchemaField field) => new() { ["aria-label"] = field.Title };

    private string FieldId(SchemaField field) => $"{IdPrefix}-{field.Name}";

    private async Task OnFieldChanged(SchemaField field, Action update)
    {
        update();
        if (validated)
        {
            field.Error = ValidateField(field);
        }

        await ValueChanged.InvokeAsync(GetValue());
    }

    private void BuildFields(JsonObject? schema, JsonObject? value)
    {
        fields.Clear();
        inputs.Clear();
        if (schema?["properties"] is not JsonObject properties)
        {
            return;
        }

        var required = schema["required"] is JsonArray array
            ? array.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var (name, node) in properties)
        {
            if (node is not JsonObject property)
            {
                continue;
            }

            var field = new SchemaField
            {
                Name = name,
                Title = GetString(property, "title") ?? name,
                Description = GetString(property, "description"),
                Required = required.Contains(name),
                MinLength = GetDecimal(property, "minLength") is { } minLength ? (int)minLength : null,
                MaxLength = GetDecimal(property, "maxLength") is { } maxLength ? (int)maxLength : null,
                Minimum = GetDecimal(property, "minimum"),
                Maximum = GetDecimal(property, "maximum"),
                Pattern = GetString(property, "pattern"),
                Format = GetString(property, "format"),
            };

            var type = GetType(property);
            var format = GetString(property, "format");
            if (property["enum"] is JsonArray values)
            {
                field.Kind = FieldKind.Enum;
                field.EnumValues = [.. values.Select(v => v is JsonValue jv && jv.GetValueKind() == JsonValueKind.String ? jv.GetValue<string>() : v?.ToJsonString() ?? string.Empty)];
            }
            else
            {
                field.Kind = (type, format) switch
                {
                    ("string", "date-time") => FieldKind.DateTime,
                    ("string", "date") => FieldKind.Date,
                    ("string", _) => field.MaxLength > LongTextThreshold ? FieldKind.LongText : FieldKind.Text,
                    ("integer", _) => FieldKind.Integer,
                    ("number", _) => FieldKind.Number,
                    ("boolean", _) => FieldKind.Boolean,
                    _ => FieldKind.Json,
                };
            }

            SetInitial(field, value?[name] ?? property["default"]);
            fields.Add(field);
        }
    }

    private static void SetInitial(SchemaField field, JsonNode? node)
    {
        if (node is null)
        {
            return;
        }

        try
        {
            switch (field.Kind)
            {
                case FieldKind.Number or FieldKind.Integer when node.GetValueKind() == JsonValueKind.Number:
                    field.NumberValue = node.GetValue<decimal>();
                    break;
                case FieldKind.Boolean when node.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                    field.BoolValue = node.GetValue<bool>();
                    break;
                case FieldKind.DateTime or FieldKind.Date when node.GetValueKind() == JsonValueKind.String:
                    field.DateValue = DateTime.TryParse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
                    break;
                case FieldKind.Json:
                    field.TextValue = WorkflowUi.FormatJson(node);
                    break;
                default:
                    field.TextValue = node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // initial value does not match the schema type; leave the field empty
        }
    }

    private static string? ValidateField(SchemaField field)
    {
        switch (field.Kind)
        {
            case FieldKind.Number or FieldKind.Integer:
                if (field.NumberValue is not { } number)
                {
                    return field.Required ? "Bu alan zorunludur." : null;
                }

                if (field.Kind == FieldKind.Integer && decimal.Truncate(number) != number)
                {
                    return "Tam sayı olmalıdır.";
                }

                if (field.Minimum is { } min && number < min)
                {
                    return $"En az {min.ToString(CultureInfo.CurrentCulture)} olmalıdır.";
                }

                if (field.Maximum is { } max && number > max)
                {
                    return $"En fazla {max.ToString(CultureInfo.CurrentCulture)} olabilir.";
                }

                return null;

            case FieldKind.Boolean:
                return null;

            case FieldKind.DateTime or FieldKind.Date:
                return field.Required && field.DateValue is null ? "Bu alan zorunludur." : null;

            case FieldKind.Json:
                if (string.IsNullOrWhiteSpace(field.TextValue))
                {
                    return field.Required ? "Bu alan zorunludur." : null;
                }

                try
                {
                    JsonNode.Parse(field.TextValue);
                    return null;
                }
                catch (JsonException)
                {
                    return "Geçerli bir JSON değeri giriniz.";
                }

            default:
                var text = field.TextValue;
                if (string.IsNullOrEmpty(text))
                {
                    return field.Required ? "Bu alan zorunludur." : null;
                }

                if (field.MinLength is { } minLength && text.Length < minLength)
                {
                    return $"En az {minLength} karakter olmalıdır.";
                }

                if (field.MaxLength is { } maxLength && text.Length > maxLength)
                {
                    return $"En fazla {maxLength} karakter olabilir.";
                }

                if (field.Kind == FieldKind.Enum && !field.EnumValues.Contains(text, StringComparer.Ordinal))
                {
                    return "Listeden bir değer seçiniz.";
                }

                if (field.Kind == FieldKind.Text && field.Format == "email" && !IsEmail(text))
                {
                    return "Geçerli bir e-posta adresi giriniz.";
                }

                if (field.Pattern is { Length: > 0 } pattern)
                {
                    try
                    {
                        if (!Regex.IsMatch(text, pattern, RegexOptions.None, PatternTimeout))
                        {
                            return "Geçersiz biçim.";
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                    {
                        // invalid or too expensive pattern: leave it to the server
                    }
                }

                return null;
        }
    }

    private static bool IsEmail(string text)
    {
        try
        {
            return EmailPattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return true; // leave it to the server
        }
    }

    private static JsonNode? ToJson(SchemaField field)
    {
        switch (field.Kind)
        {
            case FieldKind.Integer when field.NumberValue is { } integer && decimal.Truncate(integer) == integer:
                return JsonValue.Create((long)integer);
            case FieldKind.Integer or FieldKind.Number when field.NumberValue is { } number:
                return JsonValue.Create(number);
            case FieldKind.Boolean:
                return JsonValue.Create(field.BoolValue);
            case FieldKind.DateTime when field.DateValue is { } dateTime:
                return JsonValue.Create(DateTime.SpecifyKind(dateTime, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            case FieldKind.Date when field.DateValue is { } date:
                return JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case FieldKind.Json when !string.IsNullOrWhiteSpace(field.TextValue):
                try
                {
                    return JsonNode.Parse(field.TextValue);
                }
                catch (JsonException)
                {
                    return null;
                }

            case FieldKind.Text or FieldKind.LongText or FieldKind.Enum when !string.IsNullOrEmpty(field.TextValue):
                return JsonValue.Create(field.TextValue);
            default:
                return null;
        }
    }

    private static string GetType(JsonObject property)
    {
        switch (property["type"])
        {
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return value.GetValue<string>();
            case JsonArray types:
                return types.Select(t => t?.GetValueKind() == JsonValueKind.String ? t.GetValue<string>() : null)
                    .FirstOrDefault(t => t is not null and not "null") ?? "string";
            default:
                return property["properties"] is not null ? "object" : "string";
        }
    }

    private static string? GetString(JsonObject property, string name) =>
        property[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static decimal? GetDecimal(JsonObject property, string name) =>
        property[name] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<decimal>(out var number) ? number : null;

    private enum FieldKind
    {
        Text,
        LongText,
        Enum,
        DateTime,
        Date,
        Number,
        Integer,
        Boolean,
        Json,
    }

    private sealed class SchemaField
    {
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool Required { get; init; }
        public FieldKind Kind { get; set; }
        public int? MinLength { get; init; }
        public int? MaxLength { get; init; }
        public decimal? Minimum { get; init; }
        public decimal? Maximum { get; init; }
        public string? Pattern { get; init; }
        public string? Format { get; init; }
        public IReadOnlyList<string> EnumValues { get; set; } = [];

        public string? TextValue { get; set; }
        public decimal? NumberValue { get; set; }
        public bool BoolValue { get; set; }
        public DateTime? DateValue { get; set; }
        public string? Error { get; set; }
    }
}
