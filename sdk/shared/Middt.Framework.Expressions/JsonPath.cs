using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Middt.Framework.Expressions;

/// <summary>Dot-path access (<c>customer.address.city</c>) and deep merge for instance data.</summary>
public static class JsonPath
{
    public static JsonNode? Get(JsonObject root, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        JsonNode? current = root;
        foreach (var segment in path.Split('.'))
        {
            current = current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next) ? next : null;
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Sets a value at <paramref name="path"/>, creating intermediate objects.</summary>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var segments = path.Split('.');
        var current = root;
        foreach (var segment in segments[..^1])
        {
            if (current[segment] is not JsonObject child)
            {
                child = [];
                current[segment] = child;
            }

            current = child;
        }

        current[segments[^1]] = value?.Parent is null ? value : value.DeepClone();
    }

    /// <summary>
    /// Merges <paramref name="source"/> into <paramref name="target"/> (or into the object at <paramref name="path"/>):
    /// objects merge key by key recursively, everything else (including arrays) is replaced.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject source, string? path = null)
    {
        if (!string.IsNullOrEmpty(path))
        {
            if (Get(target, path) is not JsonObject nested)
            {
                nested = [];
                Set(target, path, nested);
                nested = (JsonObject)Get(target, path)!;
            }

            target = nested;
        }

        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceChild && target[key] is JsonObject targetChild)
            {
                Merge(targetChild, sourceChild);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }

    /// <summary>Converts expression results (primitives, <see cref="JsonDynamic"/>, lists) back to JSON.</summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonDynamic dyn => dyn.Node?.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        decimal d => JsonValue.Create(d),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        DateTimeOffset dto => JsonValue.Create(dto),
        DateTime dt => JsonValue.Create(dt),
        Guid g => JsonValue.Create(g),
        TimeSpan ts => JsonValue.Create(ts.ToString("c", CultureInfo.InvariantCulture)),
        IEnumerable sequence => new JsonArray([.. sequence.Cast<object?>().Select(ToNode)]),
        _ => JsonSerializer.SerializeToNode(value, value.GetType()),
    };

    public static JsonObject ParseObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? [];
}
