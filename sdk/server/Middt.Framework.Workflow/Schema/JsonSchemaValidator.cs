using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Middt.Framework.Workflow.Schema;

/// <summary>
/// Dependency free validator for the JSON Schema subset used by transition inputs:
/// <c>type</c>, <c>properties</c>, <c>required</c>, <c>additionalProperties</c> (bool), <c>enum</c>, <c>const</c>,
/// <c>minimum</c>/<c>maximum</c>, <c>minLength</c>/<c>maxLength</c>, <c>pattern</c>, <c>format</c> (date-time, email),
/// <c>items</c>, <c>minItems</c>/<c>maxItems</c>. Unknown keywords are rejected when the definition is loaded.
/// <para>
/// UI extension: <c>"x-lookup": { "entity": "Customer", "valueField": "customerId", "textField": "name" }</c> on a property
/// tells frontends to render a select whose options come from <c>api/v{version}/{entity}/getitems</c> (same shape as the
/// layout <c>lookup</c>). Its shape is checked at load time; it does not constrain the submitted value.
/// </para>
/// </summary>
public static partial class JsonSchemaValidator
{
    /// <summary>Schema keyword of the lookup UI hint.</summary>
    public const string LookupKeyword = "x-lookup";

    private static readonly HashSet<string> SupportedKeywords =
    [
        "$schema", "title", "description", "type", "properties", "required", "additionalProperties", "enum", "const",
        "minimum", "maximum", "minLength", "maxLength", "pattern", "format", "items", "minItems", "maxItems", "default",
    ];

    /// <summary>Errors in the schema itself (unsupported keywords, invalid patterns).</summary>
    public static IEnumerable<string> CheckSchema(JsonObject schema, string path = "$")
    {
        foreach (var (keyword, value) in schema)
        {
            if (keyword == LookupKeyword)
            {
                foreach (var error in CheckLookup(value, $"{path}.{LookupKeyword}"))
                {
                    yield return error;
                }
            }
            else if (!SupportedKeywords.Contains(keyword))
            {
                yield return $"{path}: unsupported schema keyword '{keyword}'.";
            }
            else if (keyword == "pattern" && !IsValidRegex(value?.GetValue<string>()))
            {
                yield return $"{path}: invalid pattern.";
            }
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, child) in properties)
            {
                if (child is JsonObject childSchema)
                {
                    foreach (var error in CheckSchema(childSchema, $"{path}.{name}"))
                    {
                        yield return error;
                    }
                }
            }
        }

        if (schema["items"] is JsonObject items)
        {
            foreach (var error in CheckSchema(items, $"{path}[]"))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> CheckLookup(JsonNode? lookup, string path)
    {
        if (lookup is not JsonObject obj)
        {
            yield return $"{path}: must be an object with entity, valueField and textField.";
            yield break;
        }

        foreach (var (name, _) in obj)
        {
            if (name is not ("entity" or "valueField" or "textField"))
            {
                yield return $"{path}: unsupported property '{name}'.";
            }
        }

        // The entity becomes a URL path segment and the fields JSON property names: restrict them to plain identifiers.
        if (!IsMatch(obj["entity"], EntityRegex()))
        {
            yield return $"{path}.entity: must be a controller name (letters and digits).";
        }

        foreach (var field in (string[])["valueField", "textField"])
        {
            if (!IsMatch(obj[field], FieldRegex()))
            {
                yield return $"{path}.{field}: must be a property name (camelCase).";
            }
        }
    }

    private static bool IsMatch(JsonNode? node, Regex regex) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && regex.IsMatch(text);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldRegex();

    public static List<string> Validate(JsonNode? instance, JsonObject schema)
    {
        var errors = new List<string>();
        Validate(instance, schema, "$", errors);
        return errors;
    }

    private static void Validate(JsonNode? node, JsonObject schema, string path, List<string> errors)
    {
        if (schema["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type) && !IsType(node, type))
        {
            errors.Add($"{path}: expected {type}.");
            return;
        }

        if (schema["enum"] is JsonArray allowed && !allowed.Any(a => JsonNode.DeepEquals(a, node)))
        {
            errors.Add($"{path}: value is not one of the allowed values.");
        }

        if (schema["const"] is { } constant && !JsonNode.DeepEquals(constant, node))
        {
            errors.Add($"{path}: value must be {constant.ToJsonString()}.");
        }

        switch (node)
        {
            case JsonObject obj:
                ValidateObject(obj, schema, path, errors);
                break;
            case JsonArray array:
                ValidateArray(array, schema, path, errors);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                ValidateString(value.GetValue<string>(), schema, path, errors);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                if (TryGetNumber(value, out var number))
                {
                    ValidateNumber(number, schema, path, errors);
                }
                else
                {
                    errors.Add($"{path}: number is out of range.");
                }

                break;
        }
    }

    private static void ValidateObject(JsonObject obj, JsonObject schema, string path, List<string> errors)
    {
        var properties = schema["properties"] as JsonObject;

        if (schema["required"] is JsonArray required)
        {
            foreach (var name in required.Select(r => r?.GetValue<string>()).OfType<string>())
            {
                if (!obj.TryGetPropertyValue(name, out var v) || v is null)
                {
                    errors.Add($"{path}.{name}: is required.");
                }
            }
        }

        var allowAdditional = schema["additionalProperties"] is not JsonValue additional || additional.GetValue<bool>();
        foreach (var (name, value) in obj)
        {
            if (properties?[name] is JsonObject propertySchema)
            {
                if (value is not null)
                {
                    Validate(value, propertySchema, $"{path}.{name}", errors);
                }
            }
            else if (!allowAdditional)
            {
                errors.Add($"{path}.{name}: is not allowed.");
            }
        }
    }

    private static void ValidateArray(JsonArray array, JsonObject schema, string path, List<string> errors)
    {
        if (Int(schema, "minItems") is { } min && array.Count < min)
        {
            errors.Add($"{path}: must contain at least {min} items.");
        }

        if (Int(schema, "maxItems") is { } max && array.Count > max)
        {
            errors.Add($"{path}: must contain at most {max} items.");
        }

        if (schema["items"] is JsonObject itemSchema)
        {
            for (var i = 0; i < array.Count; i++)
            {
                Validate(array[i], itemSchema, $"{path}[{i}]", errors);
            }
        }
    }

    private static void ValidateString(string value, JsonObject schema, string path, List<string> errors)
    {
        if (Int(schema, "minLength") is { } min && value.Length < min)
        {
            errors.Add($"{path}: must be at least {min} characters.");
        }

        if (Int(schema, "maxLength") is { } max && value.Length > max)
        {
            errors.Add($"{path}: must be at most {max} characters.");
        }

        if (schema["pattern"]?.GetValue<string>() is { } pattern && !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            errors.Add($"{path}: has an invalid format.");
        }

        var format = schema["format"]?.GetValue<string>();
        if (format == "date-time" && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            errors.Add($"{path}: must be a date-time.");
        }
        else if (format == "email" && !System.Net.Mail.MailAddress.TryCreate(value, out _))
        {
            errors.Add($"{path}: must be an e-mail address.");
        }
    }

    private static void ValidateNumber(decimal value, JsonObject schema, string path, List<string> errors)
    {
        if (schema["minimum"] is JsonValue min && value < min.GetValue<decimal>())
        {
            errors.Add($"{path}: must be ≥ {min}.");
        }

        if (schema["maximum"] is JsonValue max && value > max.GetValue<decimal>())
        {
            errors.Add($"{path}: must be ≤ {max}.");
        }
    }

    private static bool IsType(JsonNode? node, string type) => type switch
    {
        "object" => node is JsonObject,
        "array" => node is JsonArray,
        "string" => node is JsonValue v && v.GetValueKind() == JsonValueKind.String,
        "number" => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number,
        "integer" => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && TryGetNumber(v, out var n) && decimal.IsInteger(n),
        "boolean" => node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "null" => node is null,
        _ => false,
    };

    /// <summary>
    /// Reads any JSON number as decimal without throwing: values out of the decimal range (<c>1e400</c>) or backed by
    /// another CLR type (<c>double</c> from an expression) would otherwise make <c>GetValue&lt;decimal&gt;</c> throw.
    /// </summary>
    private static bool TryGetNumber(JsonValue value, out decimal number) =>
        value.TryGetValue(out number) ||
        decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static int? Int(JsonObject schema, string keyword) => schema[keyword] is JsonValue v ? v.GetValue<int>() : null;

    private static bool IsValidRegex(string? pattern)
    {
        try
        {
            _ = new Regex(pattern ?? string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
