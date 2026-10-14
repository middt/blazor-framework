using System.Text.Json;
using System.Text.Json.Serialization;

namespace Middt.Framework.Common.Helper;

/// <summary>System.Text.Json helper with lenient web defaults (camelCase, case-insensitive, nulls ignored).</summary>
public static class JsonHelper
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? value) =>
        string.IsNullOrEmpty(value) ? default : JsonSerializer.Deserialize<T>(value, Options);
}
