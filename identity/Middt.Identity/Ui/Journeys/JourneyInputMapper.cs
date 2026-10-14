using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;

namespace Middt.Identity.Ui.Journeys;

/// <summary>Kind of an input field derived from the action's JSON schema.</summary>
public enum JourneyFieldKind
{
    Text,
    Email,
    Password,
    Code,
    WholeNumber,
    Number,
    Boolean,
    Enum,
    List,
}

/// <summary>One input field of a journey action (from <see cref="AuthJourneyAction.InputSchema"/>).</summary>
public sealed record JourneyField(
    string Name,
    string Title,
    JourneyFieldKind Kind,
    bool Required,
    bool Sensitive,
    IReadOnlyList<string> EnumValues,
    string? Autocomplete,
    int? MaxLength,
    string? Description);

/// <summary>
/// Maps posted form fields to the JSON input of a journey action, typed by the action's JSON schema
/// (<c>properties.*.type</c>). Static SSR posts only strings; the workflow validates the typed input against the schema.
/// </summary>
public static class JourneyInputMapper
{
    /// <summary>Hidden field carrying the action key (a submit button with <c>name="__action"</c> overrides it).</summary>
    public const string ActionField = "__action";

    /// <summary>Optional confirmation field of <c>password</c> (checked by the UI, never sent to the journey).</summary>
    public const string ConfirmPasswordField = "confirmPassword";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        ActionField, ConfirmPasswordField, "__RequestVerificationToken", "_handler",
    };

    private static readonly string[] SecretNameParts = ["password", "secret", "code", "otp", "pin", "token"];

    /// <summary>
    /// Typed input of the posted <paramref name="form"/>: only properties declared by <paramref name="schema"/> are taken
    /// (all non-reserved fields when the schema declares none); empty values are omitted so <c>required</c> applies.
    /// </summary>
    public static JsonObject Map(JsonObject? schema, IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var properties = schema?["properties"] as JsonObject;
        var input = new JsonObject();

        foreach (var (name, values) in form)
        {
            if (Reserved.Contains(name))
            {
                continue;
            }

            JsonObject? property = null;
            if (properties is not null)
            {
                if (!properties.TryGetPropertyValue(name, out var declared))
                {
                    continue;
                }

                property = declared as JsonObject;
            }

            if (Convert(TypeOf(property), values) is { } node)
            {
                input[name] = node;
            }
        }

        // Unchecked checkboxes are not posted: declared booleans are false then.
        if (properties is not null)
        {
            foreach (var (name, property) in properties)
            {
                if (!input.ContainsKey(name) && TypeOf(property as JsonObject) == "boolean")
                {
                    input[name] = false;
                }
            }
        }

        return input;
    }

    /// <summary>Fields of an action's input schema, in declaration order.</summary>
    public static IReadOnlyList<JourneyField> Fields(JsonObject? schema)
    {
        if (schema?["properties"] is not JsonObject properties)
        {
            return [];
        }

        var required = schema["required"] is JsonArray array
            ? array.Select(n => n?.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];

        var fields = new List<JourneyField>(properties.Count);
        foreach (var (name, node) in properties)
        {
            var property = node as JsonObject;
            var sensitive = IsSensitive(name, property);
            var kind = KindOf(name, property, sensitive);
            fields.Add(new JourneyField(
                name,
                StringOf(property?["title"]) ?? name,
                kind,
                required.Contains(name),
                sensitive,
                property?["enum"] is JsonArray values ? [.. values.Select(v => v?.ToString() ?? string.Empty)] : [],
                AutocompleteOf(name, kind),
                property?["maxLength"] is JsonValue max && max.TryGetValue<int>(out var length) ? length : null,
                StringOf(property?["description"])));
        }

        return fields;
    }

    /// <summary>
    /// A field whose value must never be echoed back into the page: marked <c>x-sensitive</c>/<c>writeOnly</c>/
    /// <c>format: password</c> in the schema or named like a secret (password, code, …).
    /// </summary>
    public static bool IsSensitive(string name, JsonObject? property = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (property is not null &&
            (IsTrue(property["x-sensitive"]) || IsTrue(property["writeOnly"]) || string.Equals(StringOf(property["format"]), "password", StringComparison.Ordinal)))
        {
            return true;
        }

        return SecretNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Posted values that may be shown again after a failed submit (non-sensitive, non-reserved fields).</summary>
    public static Dictionary<string, string> SafeValues(IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in form)
        {
            if (!Reserved.Contains(name) && !IsSensitive(name) && value.LastOrDefault() is { } text)
            {
                values[name] = text;
            }
        }

        return values;
    }

    /// <summary>The action key of a post: the last <c>__action</c> value (the clicked button follows the hidden field).</summary>
    public static string? ActionOf(IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        foreach (var (name, values) in form)
        {
            if (string.Equals(name, ActionField, StringComparison.Ordinal))
            {
                return values.LastOrDefault(v => !string.IsNullOrEmpty(v));
            }
        }

        return null;
    }

    private static JsonNode? Convert(string? type, StringValues values)
    {
        if (type == "array")
        {
            var items = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => (JsonNode?)JsonValue.Create(v!.Trim())).ToArray();
            return items.Length == 0 ? null : new JsonArray(items);
        }

        var text = values.LastOrDefault();
        if (type == "boolean")
        {
            return JsonValue.Create(text is not null && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase)));
        }

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        switch (type)
        {
            case "integer" when long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                return JsonValue.Create(integer);
            case "number" when decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number):
                return JsonValue.Create(number);
            default:
                // Strings keep their exact value (passwords may contain spaces); other unparsable values stay strings
                // so the schema validation of the journey reports them.
                return JsonValue.Create(text);
        }
    }

    private static string? TypeOf(JsonObject? property) => property?["type"] switch
    {
        JsonValue value when value.TryGetValue<string>(out var type) => type,
        JsonArray types => types.Select(t => t?.ToString()).FirstOrDefault(t => t is not null and not "null"),
        _ => null,
    };

    private static JourneyFieldKind KindOf(string name, JsonObject? property, bool sensitive)
    {
        if (property?["enum"] is JsonArray)
        {
            return JourneyFieldKind.Enum;
        }

        switch (TypeOf(property))
        {
            case "boolean":
                return JourneyFieldKind.Boolean;
            case "integer":
                return JourneyFieldKind.WholeNumber;
            case "number":
                return JourneyFieldKind.Number;
            case "array":
                return JourneyFieldKind.List;
        }

        if (name.Contains("code", StringComparison.OrdinalIgnoreCase) || name.Contains("otp", StringComparison.OrdinalIgnoreCase))
        {
            return JourneyFieldKind.Code;
        }

        if (sensitive)
        {
            return JourneyFieldKind.Password;
        }

        return string.Equals(StringOf(property?["format"]), "email", StringComparison.Ordinal) || name.Contains("email", StringComparison.OrdinalIgnoreCase)
            ? JourneyFieldKind.Email
            : JourneyFieldKind.Text;
    }

    private static string? AutocompleteOf(string name, JourneyFieldKind kind) => kind switch
    {
        JourneyFieldKind.Code => "one-time-code",
        JourneyFieldKind.Password => "current-password",
        JourneyFieldKind.Email => "email",
        _ when name.Equals("userName", StringComparison.OrdinalIgnoreCase) => "username",
        _ when name.Equals("displayName", StringComparison.OrdinalIgnoreCase) => "name",
        _ => "off",
    };

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static string? StringOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
