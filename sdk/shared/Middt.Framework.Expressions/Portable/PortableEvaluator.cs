using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Middt.Framework.Expressions.Portable;

/// <summary>
/// Evaluates parsed portable expressions. Runtime values are <c>null</c>, <see cref="bool"/>, <see cref="double"/>,
/// <see cref="string"/>, <see cref="JsonObject"/>, <see cref="JsonArray"/> and the caller (<see cref="ExpressionUser"/>).
/// Evaluation never throws: anything undefined (missing member, wrong operand types, division by zero) yields <c>null</c>.
/// </summary>
internal static class PortableEvaluator
{
    public static readonly IReadOnlySet<string> Methods = new HashSet<string>(StringComparer.Ordinal)
    {
        "IsInRole", "Contains", "StartsWith", "EndsWith", "ToUpper", "ToLower", "Trim",
        "Round", "Abs", "Floor", "Ceiling", "Min", "Max",
    };

    private sealed class MathScope
    {
        public static readonly MathScope Instance = new();
    }

    public static object? Evaluate(Node node, ExpressionContext context) => node switch
    {
        LiteralNode literal => literal.Value,
        VariableNode variable => Variable(variable.Name, context),
        MemberNode member => Member(Evaluate(member.Target, context), member.Name),
        IndexNode indexer => Index(Evaluate(indexer.Target, context), Evaluate(indexer.Index, context)),
        CallNode call => Call(Evaluate(call.Target, context), call.Method, [.. call.Arguments.Select(a => Evaluate(a, context))]),
        UnaryNode unary => Unary(unary.Operator, Evaluate(unary.Operand, context)),
        BinaryNode binary => Binary(binary, context),
        ConditionalNode conditional => IsTruthy(Evaluate(conditional.Condition, context))
            ? Evaluate(conditional.WhenTrue, context)
            : Evaluate(conditional.WhenFalse, context),
        _ => null,
    };

    /// <summary>null, false, 0, NaN and "" are false; everything else (including objects and arrays) is true.</summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true,
    };

    /// <summary>Text of a value: integers without decimals, other numbers round-trip invariant, booleans lower case, objects as JSON.</summary>
    public static string ToText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        ExpressionUser user => user.Name ?? user.Id ?? string.Empty,
        MathScope => string.Empty,
        JsonNode node => node.ToJsonString(TextJson),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    // Same text as JSON.stringify in JavaScript (non-ASCII characters are not escaped).
    private static readonly JsonSerializerOptions TextJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        double d when double.IsFinite(d) && Math.Floor(d) == d && Math.Abs(d) < 9007199254740992d => JsonValue.Create((long)d),
        double d => double.IsFinite(d) ? JsonValue.Create(d) : null,
        string s => JsonValue.Create(s),
        JsonNode node => node.DeepClone(),
        ExpressionUser user => new JsonObject { ["id"] = user.Id, ["name"] = user.Name, ["roles"] = new JsonArray([.. user.Roles.Select(r => (JsonNode?)JsonValue.Create(r))]) },
        _ => null,
    };

    public static object? FromNode(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value => value.GetValueKind() switch
        {
            // (object) casts matter: without them the switch is typed JsonNode and the implicit string/bool/double → JsonNode
            // conversions would turn every primitive back into a JsonValue.
            JsonValueKind.String => (object)value.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetValue<double>(out var d) ? d
                : double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => null,
        },
        _ => (object)node, // JsonObject / JsonArray
    };

    /// <summary>
    /// Number text with the ECMAScript <c>Number.prototype.toString</c> rules (portable: identical to <c>String(n)</c> in
    /// JavaScript): shortest round-trip digits, plain notation for 1e-7 &lt; |n| &lt; 1e21, otherwise <c>1e+21</c> / <c>1e-7</c>.
    /// </summary>
    internal static string FormatNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return string.Empty;
        }

        if (d == 0)
        {
            return "0";
        }

        // Shortest round-trip digits from .NET ("R"), re-laid out with the ECMAScript algorithm.
        var r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
        var e = r.IndexOf('E', StringComparison.Ordinal);
        var exponent = e < 0 ? 0 : int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? r : r[..e];
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var fraction = dot < 0 ? string.Empty : mantissa[(dot + 1)..];
        var digits = (dot < 0 ? mantissa : mantissa[..dot]) + fraction;
        exponent -= fraction.Length;

        var trimmed = digits.TrimStart('0');
        var withoutTrailing = trimmed.TrimEnd('0');
        exponent += trimmed.Length - withoutTrailing.Length;
        digits = withoutTrailing;

        var k = digits.Length;
        var n = exponent + k; // value = 0.digits × 10^n
        var sign = d < 0 ? "-" : string.Empty;
        string text;
        if (k <= n && n <= 21)
        {
            text = digits + new string('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            text = digits[..n] + "." + digits[n..];
        }
        else if (-6 < n && n <= 0)
        {
            text = "0." + new string('0', -n) + digits;
        }
        else
        {
            var e10 = n - 1;
            var exp = (e10 < 0 ? "-" : "+") + Math.Abs(e10).ToString(CultureInfo.InvariantCulture);
            text = (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + exp;
        }

        return sign + text;
    }

    private static object? Variable(string name, ExpressionContext context) => name switch
    {
        "Math" => MathScope.Instance,
        "user" when !context.Variables.ContainsKey("user") => context.User,
        "now" when !context.Variables.ContainsKey("now") => context.Now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        _ => FromNode(context.Variables.GetValueOrDefault(name)),
    };

    private static object? Member(object? target, string name) => target switch
    {
        JsonObject obj => FromNode(obj[name]),
        JsonArray array when name is "Length" or "Count" => (double)array.Count,
        string s when name == "Length" => (double)s.Length,
        ExpressionUser user => name switch
        {
            "Name" => user.Name,
            "Id" => user.Id,
            "Roles" => new JsonArray([.. user.Roles.Select(r => (JsonNode?)JsonValue.Create(r))]),
            _ => null,
        },
        _ => null,
    };

    private static object? Index(object? target, object? key) => (target, key) switch
    {
        (JsonObject obj, string name) => FromNode(obj[name]),
        (JsonArray array, double i) when Math.Floor(i) == i && i >= 0 && i < array.Count => FromNode(array[(int)i]),
        (string s, double i) when Math.Floor(i) == i && i >= 0 && i < s.Length => s[(int)i].ToString(),
        _ => null,
    };

    private static object? Call(object? target, string method, object?[] args)
    {
        switch (target)
        {
            case ExpressionUser user when method == "IsInRole" && args is [string role]:
                return user.IsInRole(role);
            case string s:
                return (method, args) switch
                {
                    ("Contains", [string x]) => s.Contains(x, StringComparison.Ordinal),
                    ("StartsWith", [string x]) => s.StartsWith(x, StringComparison.Ordinal),
                    ("EndsWith", [string x]) => s.EndsWith(x, StringComparison.Ordinal),
                    ("ToUpper", []) => s.ToUpperInvariant(),
                    ("ToLower", []) => s.ToLowerInvariant(),
                    ("Trim", []) => s.Trim(),
                    _ => null,
                };
            case JsonArray array when method == "Contains" && args.Length == 1:
                return array.Any(item => AreEqual(FromNode(item), args[0]));
            case MathScope:
                return MathCall(method, args);
            default:
                return null;
        }
    }

    private static object? MathCall(string method, object?[] args) => (method, args) switch
    {
        ("Round", [double x]) => Math.Round(x, MidpointRounding.AwayFromZero),
        ("Round", [double x, double digits]) when digits is >= 0 and <= 15 && Math.Floor(digits) == digits
            => Math.Round(x, (int)digits, MidpointRounding.AwayFromZero),
        ("Abs", [double x]) => Math.Abs(x),
        ("Floor", [double x]) => Math.Floor(x),
        ("Ceiling", [double x]) => Math.Ceiling(x),
        ("Min", [double a, double b]) => Math.Min(a, b),
        ("Max", [double a, double b]) => Math.Max(a, b),
        _ => null,
    };

    private static object? Unary(string op, object? value) => (op, value) switch
    {
        ("!", _) => !IsTruthy(value),
        ("-", double d) => -d,
        _ => null,
    };

    private static object? Binary(BinaryNode node, ExpressionContext context)
    {
        // short-circuit operators
        switch (node.Operator)
        {
            case "&&":
                return IsTruthy(Evaluate(node.Left, context)) && IsTruthy(Evaluate(node.Right, context));
            case "||":
                return IsTruthy(Evaluate(node.Left, context)) || IsTruthy(Evaluate(node.Right, context));
            case "??":
                return Evaluate(node.Left, context) ?? Evaluate(node.Right, context);
        }

        var left = Evaluate(node.Left, context);
        var right = Evaluate(node.Right, context);
        return node.Operator switch
        {
            "==" => AreEqual(left, right),
            "!=" => !AreEqual(left, right),
            "<" or "<=" or ">" or ">=" => Compare(node.Operator, left, right),
            "+" when left is string || right is string => ToText(left) + ToText(right),
            "+" => Arithmetic(left, right, (a, b) => a + b),
            "-" => Arithmetic(left, right, (a, b) => a - b),
            "*" => Arithmetic(left, right, (a, b) => a * b),
            "/" => right is 0d ? null : Arithmetic(left, right, (a, b) => a / b),
            "%" => right is 0d ? null : Arithmetic(left, right, (a, b) => a % b),
            _ => null,
        };
    }

    private static double? Arithmetic(object? left, object? right, Func<double, double, double> op) =>
        left is double a && right is double b && op(a, b) is var result && double.IsFinite(result) ? result : null;

    /// <summary>Strict equality without coercion: <c>"5" == 5</c> is false; objects and arrays are equal only to themselves.</summary>
    private static bool AreEqual(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (double a, double b) => a == b,
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (bool a, bool b) => a == b,
        _ => ReferenceEquals(left, right),
    };

    private static bool Compare(string op, object? left, object? right)
    {
        int order;
        switch (left, right)
        {
            case (double a, double b):
                if (double.IsNaN(a) || double.IsNaN(b))
                {
                    return false;
                }

                order = a.CompareTo(b);
                break;
            case (string a, string b):
                order = string.CompareOrdinal(a, b);
                break;
            default:
                return false;
        }

        return op switch
        {
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            _ => order >= 0,
        };
    }
}
