using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Middt.Framework.Expressions;

/// <summary>
/// Read-only dynamic view over a <see cref="JsonObject"/> so expressions can write <c>data.customer.name</c>.
/// Missing members evaluate to <c>null</c>. Whole numbers surface as <see cref="long"/>, other numbers as <see cref="double"/>,
/// so rules can use natural literals (<c>data.amount &gt;= 7500.5</c>, <c>data.count == 3</c>) without type suffixes.
/// </summary>
public sealed class JsonDynamic(JsonObject? node) : DynamicObject
{
    public JsonObject? Node { get; } = node;

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = Node is not null && Node.TryGetPropertyValue(binder.Name, out var value) ? Unwrap(value) : null;
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        result = indexes is [string key] && Node is not null && Node.TryGetPropertyValue(key, out var value) ? Unwrap(value) : null;
        return true;
    }

    public override IEnumerable<string> GetDynamicMemberNames() => Node?.Select(p => p.Key) ?? [];

    public override string ToString() => Node?.ToJsonString() ?? "null";

    public static object? Unwrap(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => new JsonDynamic(obj),
        JsonArray array => new JsonDynamicArray(array),
        JsonValue value => UnwrapValue(value),
        _ => null,
    };

    private static object? UnwrapValue(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                // Values created from .NET types that serialize as strings (Guid, DateTimeOffset …) are not readable as
                // string directly: use their JSON text, so e.g. {{ instance.id }} renders the id.
                return value.TryGetValue<string>(out var text) ? text : JsonSerializer.Deserialize<string>(value.ToJsonString());
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (value.TryGetValue<long>(out var integer))
                {
                    return integer;
                }

                var number = value.TryGetValue<double>(out var dbl) ? dbl : double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
                return double.IsInteger(number) && Math.Abs(number) < long.MaxValue ? (long)number : number;
            default:
                return null;
        }
    }
}

/// <summary>
/// Read-only dynamic view over a <see cref="JsonArray"/> supporting <c>Count</c>/<c>Length</c>, indexing (including <c>[-1]</c>),
/// and non-mutating helpers (<c>Any</c>, <c>Contains</c>, <c>Find</c>, <c>Sum</c>, <c>Remove</c>, <c>RemoveAll</c>, <c>RemoveAt</c>).
/// </summary>
public sealed class JsonDynamicArray(JsonArray node) : DynamicObject, IEnumerable<object?>
{
    public JsonArray Node { get; } = node;

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = binder.Name is "Count" or "Length" ? (long)Node.Count : null;
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        if (indexes.Length == 1 && TryResolveIndex(indexes[0], Node.Count, out var index))
        {
            result = JsonDynamic.Unwrap(Node[index]);
            return true;
        }

        result = null;
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        args ??= [];
        switch (binder.Name)
        {
            case "Any" when args.Length == 0:
                result = Node.Count > 0;
                return true;

            case "Any" when args is [string field, var target]:
                result = IsHashIndexCandidate(target)
                    ? TryParseHashIndex(target, Node.Count, out _)
                    : Node.OfType<JsonObject>().Any(obj => Matches(obj[field], target));
                return true;

            case "Contains" when args.Length == 1:
                result = Node.Any(item => Matches(item, args[0]));
                return true;

            case "Find" when args is [string field, var target]:
                result = TryParseHashIndex(target, Node.Count, out var findIdx)
                    ? JsonDynamic.Unwrap(Node[findIdx])
                    : JsonDynamic.Unwrap(Node.OfType<JsonObject>().LastOrDefault(obj => Matches(obj[field], target)));
                return true;

            case "Sum" when args is [string field]:
                var sum = 0d;
                foreach (var obj in Node.OfType<JsonObject>())
                {
                    switch (JsonDynamic.Unwrap(obj[field]))
                    {
                        case long l:
                            sum += l;
                            break;
                        case double d:
                            sum += d;
                            break;
                    }
                }

                result = double.IsInteger(sum) && Math.Abs(sum) < long.MaxValue ? (long)sum : sum;
                return true;

            case "Remove" when args is [string field, var target]:
            {
                var clone = (JsonArray)Node.DeepClone();
                if (TryParseHashIndex(target, clone.Count, out var removeIdx))
                {
                    clone.RemoveAt(removeIdx);
                }
                else if (!IsHashIndexCandidate(target))
                {
                    for (var i = clone.Count - 1; i >= 0; i--)
                    {
                        if (clone[i] is JsonObject obj && Matches(obj[field], target))
                        {
                            clone.RemoveAt(i);
                            break;
                        }
                    }
                }

                result = new JsonDynamicArray(clone);
                return true;
            }

            case "RemoveAll" when args is [string field, var target]:
            {
                var clone = (JsonArray)Node.DeepClone();
                for (var i = clone.Count - 1; i >= 0; i--)
                {
                    if (clone[i] is JsonObject obj && Matches(obj[field], target))
                    {
                        clone.RemoveAt(i);
                    }
                }

                result = new JsonDynamicArray(clone);
                return true;
            }

            case "RemoveAt" when args.Length == 1:
            {
                var clone = (JsonArray)Node.DeepClone();
                if (TryResolveIndex(args[0], clone.Count, out var index))
                {
                    clone.RemoveAt(index);
                }

                result = new JsonDynamicArray(clone);
                return true;
            }

            case "Last" when args.Length == 0:
                result = Node.Count > 0 ? JsonDynamic.Unwrap(Node[^1]) : null;
                return true;

            case "Last" when args is [string field]:
                result = Node.Count > 0 && Node[^1] is JsonObject lastObj ? JsonDynamic.Unwrap(lastObj[field]) : null;
                return true;

            default:
                result = null;
                return false;
        }
    }

    public override string ToString() => Node.ToJsonString();

    public IEnumerator<object?> GetEnumerator() => Node.Select(JsonDynamic.Unwrap).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool TryResolveIndex(object? raw, int count, out int index)
    {
        var parsed = raw switch
        {
            int i => (long?)i,
            long l => l,
            double d when double.IsInteger(d) => (long)d,
            _ => null,
        };
        if (parsed is { } idx)
        {
            var resolved = idx < 0 ? count + idx : idx;
            if (resolved >= 0 && resolved < count)
            {
                index = (int)resolved;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool IsHashIndexCandidate(object? raw)
    {
        var text = raw switch
        {
            string s => s,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => null,
        };
        return text is { Length: >= 2 } && text[0] == '#' && char.IsAsciiDigit(text[1]);
    }

    private static bool TryParseHashIndex(object? raw, int count, out int index)
    {
        var text = raw switch
        {
            string s => s,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => null,
        };
        if (!string.IsNullOrEmpty(text) && text[0] == '#')
        {
            var len = 0;
            while (1 + len < text.Length && char.IsAsciiDigit(text[1 + len]))
            {
                len++;
            }

            if (len > 0 && int.TryParse(text.AsSpan(1, len), CultureInfo.InvariantCulture, out var oneBased))
            {
                var zeroBased = oneBased - 1;
                if (zeroBased >= 0 && zeroBased < count)
                {
                    index = zeroBased;
                    return true;
                }
            }
        }

        index = -1;
        return false;
    }

    private static bool Matches(JsonNode? node, object? target)
    {
        var left = JsonDynamic.Unwrap(node);
        var right = target is JsonNode jn ? JsonDynamic.Unwrap(jn) : target;
        return (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (long a, long b) => a == b,
            (long a, int b) => a == b,
            (long a, double b) => a == b,
            (double a, long b) => a == b,
            (double a, int b) => a == b,
            (double a, double b) => a == b,
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (bool a, bool b) => a == b,
            _ => Equals(left, right),
        };
    }
}
