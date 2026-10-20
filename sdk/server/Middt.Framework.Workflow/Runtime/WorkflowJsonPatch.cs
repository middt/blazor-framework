using System.Text.Json.Nodes;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Minimal RFC 6902 JSON Patch over instance data: <see cref="Diff"/> produces <c>add</c> / <c>replace</c> / <c>remove</c>
/// operations (objects are walked member by member; arrays and scalars are replaced whole), <see cref="Apply"/> plays them
/// back. Used by <c>history: delta</c> rows and to merge a background task's changes into data that moved on meanwhile.
/// </summary>
public static class WorkflowJsonPatch
{
    /// <summary>Operations that turn <paramref name="from"/> into <paramref name="to"/> (empty when they are equal).</summary>
    public static JsonArray Diff(JsonObject from, JsonObject to)
    {
        var operations = new JsonArray();
        DiffObject(from, to, string.Empty, operations);
        return operations;
    }

    /// <summary>Applies <paramref name="patch"/> to <paramref name="target"/> in place; missing parents are created.</summary>
    public static void Apply(JsonObject target, JsonArray patch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(patch);
        foreach (var node in patch)
        {
            if (node is not JsonObject operation)
            {
                throw new FormatException("A JSON Patch operation must be an object.");
            }

            var op = operation["op"]?.GetValue<string>() ?? throw new FormatException("A JSON Patch operation needs 'op'.");
            var path = operation["path"]?.GetValue<string>() ?? throw new FormatException("A JSON Patch operation needs 'path'.");
            var segments = Parse(path);
            if (segments.Count == 0)
            {
                throw new FormatException("Replacing the whole document is not supported.");
            }

            var parent = target;
            foreach (var segment in segments.Take(segments.Count - 1))
            {
                if (parent[segment] is not JsonObject child)
                {
                    child = [];
                    parent[segment] = child;
                }

                parent = child;
            }

            var name = segments[^1];
            switch (op)
            {
                case "add" or "replace":
                    parent[name] = operation["value"]?.DeepClone();
                    break;
                case "remove":
                    parent.Remove(name);
                    break;
                default:
                    throw new FormatException($"Unsupported JSON Patch operation '{op}'.");
            }
        }
    }

    private static void DiffObject(JsonObject from, JsonObject to, string path, JsonArray operations)
    {
        foreach (var (name, fromValue) in from)
        {
            if (!to.ContainsKey(name))
            {
                operations.Add(new JsonObject { ["op"] = "remove", ["path"] = path + "/" + Escape(name) });
                continue;
            }

            var toValue = to[name];
            var childPath = path + "/" + Escape(name);
            if (fromValue is JsonObject fromObject && toValue is JsonObject toObject)
            {
                DiffObject(fromObject, toObject, childPath, operations);
            }
            else if (!JsonNode.DeepEquals(fromValue, toValue))
            {
                operations.Add(new JsonObject { ["op"] = "replace", ["path"] = childPath, ["value"] = toValue?.DeepClone() });
            }
        }

        foreach (var (name, toValue) in to)
        {
            if (!from.ContainsKey(name))
            {
                operations.Add(new JsonObject { ["op"] = "add", ["path"] = path + "/" + Escape(name), ["value"] = toValue?.DeepClone() });
            }
        }
    }

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static List<string> Parse(string pointer)
    {
        if (pointer.Length == 0)
        {
            return [];
        }

        if (pointer[0] != '/')
        {
            throw new FormatException($"Invalid JSON Pointer '{pointer}'.");
        }

        return [.. pointer[1..].Split('/').Select(s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];
    }
}
