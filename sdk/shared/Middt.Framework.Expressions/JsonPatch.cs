using System.Globalization;
using System.Text.Json.Nodes;

namespace Middt.Framework.Expressions;

/// <summary>
/// RFC 6902 JSON Patch over JSON documents, shared by the backend and the .NET frontends (the TypeScript twin is
/// <c>jsonPatchDiff</c> in <c>@middt/ui-core</c>; both are checked by <c>spec/json-patch.conformance.json</c>).
/// <list type="bullet">
/// <item><see cref="Diff"/>: the operations that turn one object into another – objects are walked member by member,
/// arrays and scalars are replaced whole. Used by <c>history: delta</c>, background task merges and frontends that send
/// a form as a patch.</item>
/// <item><see cref="ApplyStrict"/>: applies a client patch exactly as RFC 6902 says (<c>add</c>, <c>remove</c>,
/// <c>replace</c>, <c>move</c>, <c>copy</c>, <c>test</c>; array indexes and <c>-</c>); any invalid operation fails the whole
/// patch and leaves the document unchanged.</item>
/// <item><see cref="Apply"/>: plays back patches produced by <see cref="Diff"/> (lenient: missing parents are created).</item>
/// </list>
/// </summary>
public static class JsonPatch
{
    /// <summary>Operations that turn <paramref name="from"/> into <paramref name="to"/> (empty when they are equal).</summary>
    public static JsonArray Diff(JsonObject from, JsonObject to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var operations = new JsonArray();
        DiffObject(from, to, string.Empty, operations);
        return operations;
    }

    /// <summary>Applies a patch produced by <see cref="Diff"/> to <paramref name="target"/> in place; missing parents are created.</summary>
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

    /// <summary>
    /// The structural problems of <paramref name="patch"/> (not an array, unknown <c>op</c>, missing <c>path</c> /
    /// <c>value</c> / <c>from</c>, invalid JSON Pointer), each prefixed with the operation index; empty when well formed.
    /// </summary>
    public static IReadOnlyList<string> Validate(JsonNode? patch)
    {
        var errors = new List<string>();
        if (patch is not JsonArray operations)
        {
            errors.Add("The body must be a JSON Patch: an array of operations.");
            return errors;
        }

        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is not JsonObject operation)
            {
                errors.Add($"[{i}]: an operation must be an object.");
                continue;
            }

            var op = Text(operation, "op");
            if (op is not ("add" or "remove" or "replace" or "move" or "copy" or "test"))
            {
                errors.Add($"[{i}]: 'op' must be add, remove, replace, move, copy or test.");
                continue;
            }

            if (!IsPointer(Text(operation, "path")))
            {
                errors.Add($"[{i}]: 'path' must be a JSON Pointer (\"\" or starting with \"/\").");
            }

            if (op is "add" or "replace" or "test" && !operation.ContainsKey("value"))
            {
                errors.Add($"[{i}]: '{op}' needs a 'value'.");
            }

            if (op is "move" or "copy" && !IsPointer(Text(operation, "from")))
            {
                errors.Add($"[{i}]: '{op}' needs a 'from' JSON Pointer.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Applies a client supplied <paramref name="patch"/> to <paramref name="document"/> following RFC 6902. The patch is
    /// atomic: on the first failing operation a <see cref="JsonPatchException"/> is thrown and <paramref name="document"/>
    /// is left as it was. The document root cannot be replaced or removed (it stays an object).
    /// </summary>
    public static void ApplyStrict(JsonObject document, JsonArray patch)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Validate(patch) is { Count: > 0 } errors)
        {
            throw new JsonPatchException(string.Join(" ", errors));
        }

        var working = (JsonObject)document.DeepClone();
        for (var i = 0; i < patch.Count; i++)
        {
            var operation = (JsonObject)patch[i]!;
            var op = Text(operation, "op")!;
            var pointer = Text(operation, "path")!;
            var path = Parse(pointer);
            try
            {
                switch (op)
                {
                    case "add":
                        Add(working, path, operation["value"]?.DeepClone());
                        break;
                    case "remove":
                        Remove(working, path);
                        break;
                    case "replace":
                        Replace(working, path, operation["value"]?.DeepClone());
                        break;
                    case "move":
                        var from = Parse(Text(operation, "from")!);
                        if (path.Count > from.Count && from.SequenceEqual(path.Take(from.Count), StringComparer.Ordinal))
                        {
                            throw new JsonPatchException("a value cannot be moved into one of its own children.");
                        }

                        var moved = Get(working, from);
                        Remove(working, from);
                        Add(working, path, moved?.DeepClone());
                        break;
                    case "copy":
                        Add(working, path, Get(working, Parse(Text(operation, "from")!))?.DeepClone());
                        break;
                    case "test":
                        if (!JsonNode.DeepEquals(Get(working, path), operation["value"]))
                        {
                            throw new JsonPatchException("the value is not the expected one.", isTestFailure: true);
                        }

                        break;
                }
            }
            catch (JsonPatchException ex)
            {
                throw new JsonPatchException($"[{i}] {op} '{pointer}': {ex.Message}", ex.IsTestFailure);
            }
        }

        document.Clear();
        foreach (var (name, value) in working.ToList())
        {
            working.Remove(name);
            document[name] = value;
        }
    }

    // ───────────────────────────── strict helpers ─────────────────────────────

    private static JsonNode? Get(JsonObject root, List<string> path)
    {
        JsonNode? current = root;
        for (var i = 0; i < path.Count; i++)
        {
            var segment = path[i];
            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segment, out var child) => child,
                JsonArray array when Index(segment, array.Count, allowEnd: false) is { } index => array[index],
                _ => throw new JsonPatchException($"'{Pointer(path.Take(i + 1))}' does not exist."),
            };
        }

        return current;
    }

    private static JsonNode Parent(JsonObject root, List<string> path)
    {
        if (path.Count == 0)
        {
            throw new JsonPatchException("the document root cannot be replaced or removed.");
        }

        return Get(root, path[..^1]) is { } parent and (JsonObject or JsonArray)
            ? parent
            : throw new JsonPatchException($"the parent of '{Pointer(path)}' is not an object or an array.");
    }

    private static void Add(JsonObject root, List<string> path, JsonNode? value)
    {
        var parent = Parent(root, path);
        var name = path[^1];
        if (parent is JsonObject obj)
        {
            obj[name] = value;
            return;
        }

        var array = (JsonArray)parent;
        var index = Index(name, array.Count, allowEnd: true)
            ?? throw new JsonPatchException($"'{name}' is not a valid index for an array of {array.Count}.");
        array.Insert(index, value);
    }

    /// <summary>Replaces an existing value in place (members keep their position).</summary>
    private static void Replace(JsonObject root, List<string> path, JsonNode? value)
    {
        var parent = Parent(root, path);
        var name = path[^1];
        if (parent is JsonObject obj)
        {
            if (!obj.ContainsKey(name))
            {
                throw new JsonPatchException($"'{Pointer(path)}' does not exist.");
            }

            obj[name] = value;
            return;
        }

        var array = (JsonArray)parent;
        var index = Index(name, array.Count, allowEnd: false) ?? throw new JsonPatchException($"'{Pointer(path)}' does not exist.");
        array[index] = value;
    }

    private static void Remove(JsonObject root, List<string> path)
    {
        var parent = Parent(root, path);
        var name = path[^1];
        if (parent is JsonObject obj)
        {
            if (!obj.Remove(name))
            {
                throw new JsonPatchException($"'{Pointer(path)}' does not exist.");
            }

            return;
        }

        var array = (JsonArray)parent;
        var index = Index(name, array.Count, allowEnd: false) ?? throw new JsonPatchException($"'{Pointer(path)}' does not exist.");
        array.RemoveAt(index);
    }

    /// <summary>An array index: digits without leading zeros below <paramref name="count"/> (or equal to it / <c>-</c> when appending).</summary>
    private static int? Index(string segment, int count, bool allowEnd)
    {
        if (segment == "-")
        {
            return allowEnd ? count : null;
        }

        if (segment.Length == 0 || (segment.Length > 1 && segment[0] == '0') || !segment.All(char.IsAsciiDigit)
            || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return null;
        }

        return index < count || (allowEnd && index == count) ? index : null;
    }

    private static string? Text(JsonObject operation, string name) =>
        operation[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool IsPointer(string? pointer) => pointer is not null && (pointer.Length == 0 || pointer[0] == '/');

    private static string Pointer(IEnumerable<string> path) => string.Concat(path.Select(s => "/" + Escape(s)));

    // ───────────────────────────── diff ─────────────────────────────

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

/// <summary>A JSON Patch that cannot be applied; <see cref="IsTestFailure"/> marks a failed <c>test</c> operation (a conflict).</summary>
public sealed class JsonPatchException(string message, bool isTestFailure = false) : Exception(message)
{
    public bool IsTestFailure { get; } = isTestFailure;
}
