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
            if (segment.EndsWith("[-1]", StringComparison.Ordinal))
            {
                var arrayKey = segment[..^4];
                current = current is JsonObject arrParent
                    && arrParent.TryGetPropertyValue(arrayKey, out var arrNode)
                    && arrNode is JsonArray { Count: > 0 } arr
                    ? arr[^1]
                    : null;
            }
            else
            {
                current = current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next) ? next : null;
            }

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Sets a value at <paramref name="path"/>, creating intermediate objects (or appending to a <see cref="JsonArray"/> when a segment ends with <c>[]</c>).</summary>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var segments = path.Split('.');
        var current = root;
        foreach (var segment in segments[..^1])
        {
            if (segment.EndsWith("[-1]", StringComparison.Ordinal))
            {
                var arrayKey = segment[..^4];
                if (current[arrayKey] is JsonArray { Count: > 0 } existingArr && existingArr[^1] is JsonObject lastObj)
                {
                    current = lastObj;
                    continue;
                }

                if (current[arrayKey] is not JsonArray newArr)
                {
                    newArr = [];
                    current[arrayKey] = newArr;
                }

                var created = new JsonObject();
                newArr.Add(created);
                current = created;
                continue;
            }

            if (segment.EndsWith("[]", StringComparison.Ordinal))
            {
                var arrayKey = segment[..^2];
                if (current[arrayKey] is not JsonArray arr)
                {
                    arr = [];
                    current[arrayKey] = arr;
                }

                var created = new JsonObject();
                arr.Add(created);
                current = created;
                continue;
            }

            if (current[segment] is not JsonObject child)
            {
                child = [];
                current[segment] = child;
            }

            current = child;
        }

        var leaf = segments[^1];
        var cloned = value?.Parent is null ? value : value.DeepClone();
        if (leaf.EndsWith("[]", StringComparison.Ordinal))
        {
            var arrayKey = leaf[..^2];
            if (current[arrayKey] is not JsonArray arr)
            {
                arr = [];
                current[arrayKey] = arr;
            }

            arr.Add(cloned);
        }
        else if (leaf.EndsWith("[-1]", StringComparison.Ordinal))
        {
            var arrayKey = leaf[..^4];
            if (current[arrayKey] is JsonArray { Count: > 0 } arr)
            {
                arr[^1] = cloned;
            }
            else
            {
                current[arrayKey] = new JsonArray { cloned };
            }
        }
        else
        {
            current[leaf] = cloned;
        }
    }

    /// <summary>
    /// Merges <paramref name="source"/> into <paramref name="target"/> (or into the object at <paramref name="path"/>;
    /// when <paramref name="path"/> ends with <c>[]</c>, appends <paramref name="source"/> to the array at that path):
    /// objects merge key by key recursively, everything else (including arrays) is replaced.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject source, string? path = null)
    {
        if (!string.IsNullOrEmpty(path))
        {
            if (path.EndsWith("[]", StringComparison.Ordinal))
            {
                Set(target, path, source.DeepClone());
                return;
            }

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
        JsonDynamicArray arr => arr.Node.DeepClone(),
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
