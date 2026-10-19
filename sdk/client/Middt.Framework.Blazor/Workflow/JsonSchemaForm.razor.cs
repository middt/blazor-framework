using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Blazor.Ui;
using Middt.Framework.Ui.Model;
using Radzen;

namespace Middt.Framework.Blazor.Workflow;

/// <summary>
/// Renders an input form for a JSON Schema subset (<c>type: object</c> with flat <c>properties</c>):
/// string (text, long text, enum, date/date-time, e-mail), number/integer and boolean; nested objects/arrays fall back
/// to a JSON text area. The value is exposed as a <see cref="JsonObject"/>; call <see cref="Validate"/> before
/// submitting to show required/length/range errors next to the fields.
/// A property with <c>"x-lookup": { "entity", "valueField", "textField" }</c> is a select whose options are the items of
/// the entity (same shape as the layout field <c>lookup</c>; loaded through <see cref="UiLookupCache"/>); the selected
/// value is written with the property's <c>type</c> (integer/number → JSON number, otherwise string).
/// </summary>
public partial class JsonSchemaForm : ComponentBase, IDisposable
{
    /// <summary>Shown when the options of an <c>x-lookup</c> field cannot be loaded.</summary>
    public const string LookupLoadError = "Liste yüklenemedi.";

    private const int LongTextThreshold = 200;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@.\s]+(?:\.[^@.\s]+)+$", RegexOptions.CultureInvariant, PatternTimeout);

    private readonly List<SchemaField> fields = [];
    private readonly Dictionary<string, IRadzenFormComponent> inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LookupState> lookups = new(StringComparer.Ordinal);
    private JsonObject? renderedSchema;
    private CancellationTokenSource? lookupCancellation;
    private bool validated;

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

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

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(Schema, renderedSchema))
        {
            renderedSchema = Schema;
            validated = false;
            BuildFields(Schema, Value);
            await LoadLookupsAsync();
        }
    }

    /// <summary>Loads the options of the <c>x-lookup</c> fields (shared per circuit by <see cref="UiLookupCache"/>).</summary>
    private async Task LoadLookupsAsync()
    {
        if (lookupCancellation is not null)
        {
            await lookupCancellation.CancelAsync();
            lookupCancellation.Dispose();
        }

        lookups.Clear();
        var lookupFields = fields.Where(f => f.Lookup is not null).ToList();
        if (lookupFields.Count == 0)
        {
            lookupCancellation = null;
            return;
        }

        var cancellation = lookupCancellation = new CancellationTokenSource();
        var cache = Services.GetService<UiLookupCache>();
        foreach (var field in lookupFields)
        {
            lookups[field.Name] = new LookupState();
        }

        foreach (var field in lookupFields)
        {
            var lookup = field.Lookup!;
            LookupState state;
            try
            {
                var items = cache is null
                    ? throw new InvalidOperationException("UiLookupCache is not registered (AddMiddtUi).")
                    : await cache.GetAllAsync(lookup.Entity, cancellation.Token);
                state = new LookupState { Options = BuildLookupOptions(lookup, items) };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return; // schema changed or the form was disposed
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or HttpRequestException)
            {
                state = new LookupState { Error = LookupLoadError };
            }

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            lookups[field.Name] = state;
        }
    }

    private static List<UiOption> BuildLookupOptions(SchemaLookup lookup, List<JsonObject> items)
    {
        var options = new List<UiOption>(items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var rawValue = item[lookup.ValueField];
            var key = UiFieldJson.Key(rawValue);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            var text = UiFieldJson.Key(item[lookup.TextField]);
            options.Add(new UiOption(rawValue?.DeepClone(), text.Length == 0 ? key : text));
        }

        return options;
    }

    /// <summary>Options of a lookup select; a prefilled value missing from the loaded items stays selectable (shown as is).</summary>
    private static List<UiOption> LookupOptions(SchemaField field, LookupState state) =>
        field.TextValue is { Length: > 0 } value && !state.Options.Exists(o => o.Key == value)
            ? [new UiOption(JsonValue.Create(value), value), .. state.Options]
            : state.Options;

    private LookupState LookupStateOf(SchemaField field) => lookups.TryGetValue(field.Name, out var state) ? state : LookupState.Empty;

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

    /// <summary>Clears the entered values and errors; <paramref name="value"/> prefills the fields (like <see cref="Value"/>).</summary>
    public void Reset(JsonObject? value = null)
    {
        validated = false;
        BuildFields(Schema, value);
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
            field.Type = type;
            if (GetLookup(property) is { } lookup)
            {
                field.Kind = FieldKind.Lookup;
                field.Lookup = lookup;
            }
            else if (property["enum"] is JsonArray values)
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

            case FieldKind.Lookup:
                return field.Required && string.IsNullOrEmpty(field.TextValue) ? "Bu alan zorunludur." : null;

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

            case FieldKind.Lookup when !string.IsNullOrEmpty(field.TextValue):
                return WorkflowUi.LookupValue(field.TextValue, field.Type);
            case FieldKind.Text or FieldKind.LongText or FieldKind.Enum when !string.IsNullOrEmpty(field.TextValue):
                return JsonValue.Create(field.TextValue);
            default:
                return null;
        }
    }

    private static string GetType(JsonObject property) => WorkflowUi.SchemaType(property);

    /// <summary>Reads <c>x-lookup</c>; incomplete definitions are ignored (the property renders by its type).</summary>
    private static SchemaLookup? GetLookup(JsonObject property) =>
        property["x-lookup"] is JsonObject lookup
        && GetString(lookup, "entity") is { Length: > 0 } entity
        && GetString(lookup, "valueField") is { Length: > 0 } valueField
        && GetString(lookup, "textField") is { Length: > 0 } textField
            ? new SchemaLookup(entity, valueField, textField)
            : null;

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
        Lookup,
    }

    private sealed record SchemaLookup(string Entity, string ValueField, string TextField);

    private sealed class LookupState
    {
        public static readonly LookupState Empty = new();

        public List<UiOption> Options { get; init; } = [];
        public string? Error { get; init; }
    }

    private sealed class SchemaField
    {
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool Required { get; init; }
        public FieldKind Kind { get; set; }
        public string Type { get; set; } = "string";
        public SchemaLookup? Lookup { get; set; }
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

    public void Dispose()
    {
        lookupCancellation?.Cancel();
        lookupCancellation?.Dispose();
        lookupCancellation = null;
        GC.SuppressFinalize(this);
    }
}
