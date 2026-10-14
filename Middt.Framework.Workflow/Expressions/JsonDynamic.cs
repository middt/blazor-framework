using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Middt.Framework.Workflow.Expressions;

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
        JsonArray array => array.Select(Unwrap).ToList(),
        JsonValue value => UnwrapValue(value),
        _ => null,
    };

    private static object? UnwrapValue(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                return value.GetValue<string>();
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
