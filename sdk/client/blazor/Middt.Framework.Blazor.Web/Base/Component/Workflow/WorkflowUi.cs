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
}
