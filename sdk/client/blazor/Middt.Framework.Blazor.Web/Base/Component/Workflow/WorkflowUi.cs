using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Middt.Framework.Model.Workflow;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Component.Workflow;

/// <summary>Display helpers shared by the workflow components.</summary>
public static class WorkflowUi
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Converts the selected key of an <c>x-lookup</c> select to the JSON value of the schema type: <c>integer</c> and
    /// <c>number</c> become JSON numbers (when the key parses), everything else a string.
    /// </summary>
    public static JsonNode? LookupValue(string? key, string? type)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return type switch
        {
            "integer" when long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => JsonValue.Create(integer),
            "integer" or "number" when decimal.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
            _ => JsonValue.Create(key),
        };
    }

    /// <summary>Query parameter that opens the start dialog (<c>?start=1</c>); never a start input value.</summary>
    public const string StartQueryParameter = "start";

    /// <summary>Query parameter that prefills the business key of the start dialog (<c>?key=…</c>).</summary>
    public const string StartKeyQueryParameter = "key";

    /// <summary>
    /// Names of the query parameters that prefill the start input: every name that is a property of
    /// <paramref name="schema"/> except <c>start</c> and <c>key</c> (unknown names are ignored). These are removed from
    /// the URL together with <c>start</c>/<c>key</c> once the dialog was opened.
    /// </summary>
    public static IReadOnlyList<string> StartPrefillParameters(JsonObject? schema, IEnumerable<string>? names)
    {
        if (names is null || schema?["properties"] is not JsonObject properties || properties.Count == 0)
        {
            return [];
        }

        return [.. names
            .Where(n => n is not (StartQueryParameter or StartKeyQueryParameter) && properties[n] is JsonObject)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Start dialog prefill from URL query values (e.g. <c>?start=1&amp;key=category-5&amp;categoryId=5</c>): each
    /// value whose name is a property of <paramref name="schema"/> (see <see cref="StartPrefillParameters"/>) converted by
    /// the property type (<see cref="QueryValue"/>). Values that do not convert are skipped. <c>null</c> when nothing applies.
    /// </summary>
    public static JsonObject? StartPrefill(JsonObject? schema, IEnumerable<KeyValuePair<string, string?>>? values)
    {
        if (values is null || schema?["properties"] is not JsonObject properties)
        {
            return null;
        }

        var list = values.ToList();
        var result = new JsonObject();
        foreach (var name in StartPrefillParameters(schema, list.Select(v => v.Key)))
        {
            // the first value wins when a parameter is repeated (same as @middt/ui-core workflowStartQuery)
            var text = list.First(v => v.Key == name).Value;
            if (QueryValue(text, SchemaType(properties[name]!.AsObject())) is { } node)
            {
                result[name] = node;
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Converts a query string value to the JSON value of a schema type: <c>integer</c> → JSON integer, <c>number</c> →
    /// JSON number, <c>boolean</c> → <c>true</c>/<c>false</c> (also <c>1</c>/<c>0</c>), <c>string</c> → string;
    /// <c>null</c> for empty values, values that do not parse and object/array types.
    /// </summary>
    public static JsonNode? QueryValue(string? text, string? type)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        switch (type)
        {
            case "integer":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    ? JsonValue.Create(integer)
                    : null;
            case "number":
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? JsonValue.Create(number)
                    : null;
            case "boolean":
                return text.ToUpperInvariant() switch
                {
                    "TRUE" or "1" => JsonValue.Create(true),
                    "FALSE" or "0" => JsonValue.Create(false),
                    _ => null,
                };
            case "object" or "array":
                return null;
            default:
                return JsonValue.Create(text);
        }
    }

    /// <summary>
    /// The JSON Schema type of a property: <c>type</c> (the first non-<c>null</c> entry of a type array); without a type
    /// <c>object</c> when it declares <c>properties</c>, otherwise <c>string</c>.
    /// </summary>
    public static string SchemaType(JsonObject property)
    {
        ArgumentNullException.ThrowIfNull(property);
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

    public static string StatusText(WorkflowInstanceStatus status) => status switch
    {
        WorkflowInstanceStatus.Active => "Aktif",
        WorkflowInstanceStatus.Busy => "İşleniyor",
        WorkflowInstanceStatus.Completed => "Tamamlandı",
        WorkflowInstanceStatus.Faulted => "Hatalı",
        WorkflowInstanceStatus.Cancelled => "İptal edildi",
        _ => status.ToString(),
    };

    public static BadgeStyle StatusBadge(WorkflowInstanceStatus status) => status switch
    {
        WorkflowInstanceStatus.Active => BadgeStyle.Info,
        WorkflowInstanceStatus.Busy => BadgeStyle.Warning,
        WorkflowInstanceStatus.Completed => BadgeStyle.Success,
        WorkflowInstanceStatus.Faulted => BadgeStyle.Danger,
        _ => BadgeStyle.Secondary,
    };

    public static string SubTypeText(string? subType) => subType switch
    {
        null or "" or "None" => string.Empty,
        "Human" => "Kullanıcı işlemi bekleniyor",
        "Success" => "Başarılı",
        "Error" => "Hata / Red",
        "Cancelled" => "İptal",
        "Timeout" => "Zaman aşımı",
        _ => subType,
    };

    public static IReadOnlyList<KeyValuePair<WorkflowInstanceStatus, string>> StatusOptions { get; } =
        [.. Enum.GetValues<WorkflowInstanceStatus>().Select(s => new KeyValuePair<WorkflowInstanceStatus, string>(s, StatusText(s)))];

    /// <summary>
    /// Formats a server timestamp (UTC) in <paramref name="timeZone"/> (the browser's zone, see
    /// <see cref="GetBrowserTimeZoneAsync"/>), falling back to the server's local time zone.
    /// </summary>
    public static string FormatDate(DateTime? value, TimeZoneInfo? timeZone = null)
    {
        if (value is not { } date)
        {
            return string.Empty;
        }

        var utc = date.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => date,
        };
        var local = timeZone is null ? utc.ToLocalTime() : TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        return local.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Resolves the browser's time zone (via <c>middt.timeZone</c> in framework.js); <c>null</c> when unknown.</summary>
    public static async Task<TimeZoneInfo?> GetBrowserTimeZoneAsync(IJSRuntime jsRuntime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        try
        {
            var id = await jsRuntime.InvokeAsync<string?>("middt.timeZone", cancellationToken);
            return string.IsNullOrEmpty(id) ? null : TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is JSException or TimeZoneNotFoundException or InvalidTimeZoneException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Initial value of a step form: the data stored at <paramref name="inputInto"/> (dot path; root when empty) when the step
    /// is revisited, limited to the properties declared by <paramref name="schema"/>. <c>null</c> when nothing is stored.
    /// </summary>
    public static JsonObject? StepPrefill(JsonObject? data, string? inputInto, JsonObject? schema)
    {
        if (data is null || schema?["properties"] is not JsonObject properties || properties.Count == 0)
        {
            return null;
        }

        JsonNode? source = data;
        if (!string.IsNullOrWhiteSpace(inputInto))
        {
            foreach (var segment in inputInto.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                source = source is JsonObject obj && obj.TryGetPropertyValue(segment, out var next) ? next : null;
                if (source is null)
                {
                    return null;
                }
            }
        }

        if (source is not JsonObject values)
        {
            return null;
        }

        var result = new JsonObject();
        foreach (var (name, _) in properties)
        {
            if (values.TryGetPropertyValue(name, out var value) && value is not null)
            {
                result[name] = value.DeepClone();
            }
        }

        return result.Count > 0 ? result : null;
    }

    public static string FormatJson(JsonNode? node) => node is null ? "{}" : node.ToJsonString(IndentedJson);

    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Extracts columns/fields from <see cref="WorkflowDefinitionModel.DataSchema"/> (falling back to
    /// <see cref="WorkflowDefinitionModel.StartInputSchema"/>), excluding complex object/array properties or (when
    /// <paramref name="includeAll"/> is <c>false</c>) properties with <c>"x-list": false</c>.
    /// </summary>
    public static IReadOnlyList<WorkflowDataColumn> DataColumns(WorkflowDefinitionModel? definition, bool includeAll = false)
    {
        var schema = definition?.DataSchema ?? definition?.StartInputSchema;
        if (schema?["properties"] is not JsonObject properties || properties.Count == 0)
        {
            return [];
        }

        var columns = new List<WorkflowDataColumn>(properties.Count);
        foreach (var (name, node) in properties)
        {
            if (node is not JsonObject prop)
            {
                continue;
            }

            if (!includeAll && prop["x-list"] is JsonValue listFlag && listFlag.TryGetValue<bool>(out var show) && !show)
            {
                continue;
            }

            var type = SchemaType(prop);
            if (type is "object" or "array")
            {
                continue;
            }

            var title = prop["title"] is JsonValue titleNode && titleNode.GetValueKind() == JsonValueKind.String
                ? titleNode.GetValue<string>()
                : name;
            columns.Add(new WorkflowDataColumn(name, string.IsNullOrWhiteSpace(title) ? name : title, type));
        }

        return columns;
    }

    /// <summary>Formats a projected workflow data property value for display in the instance grid.</summary>
    public static string FormatDataCell(JsonObject? data, WorkflowDataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (data is null || !data.TryGetPropertyValue(column.Key, out var node) || node is null)
        {
            return "-";
        }

        if (node is JsonValue val)
        {
            return val.GetValueKind() switch
            {
                JsonValueKind.String => val.GetValue<string>() is { Length: > 0 } s ? s : "-",
                JsonValueKind.True => "Evet",
                JsonValueKind.False => "Hayır",
                JsonValueKind.Number when column.Type == "number" && val.TryGetValue<decimal>(out var dec) =>
                    dec.ToString(dec == decimal.Truncate(dec) ? "N0" : "N2", TrCulture),
                JsonValueKind.Number when val.TryGetValue<long>(out var l) =>
                    l.ToString("N0", TrCulture),
                _ => val.ToJsonString(),
            };
        }

        return node.ToJsonString();
    }
}

/// <summary>Column definition extracted from a workflow's JSON data schema for instance list grids.</summary>
public sealed record WorkflowDataColumn(string Key, string Title, string Type);

