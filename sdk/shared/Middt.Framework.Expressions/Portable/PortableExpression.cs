using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Middt.Framework.Expressions.Portable;

/// <summary>
/// The <b>portable expression language</b>: a small, sandboxed language with identical semantics in .NET and in the
/// TypeScript client (<c>sdk/client/typescript/ui-core</c>), so that any frontend (Blazor, Angular, static HTML) can evaluate
/// layouts. The shared conformance suite <c>spec/expressions.conformance.json</c> is run by both implementations.
/// <list type="bullet">
/// <item>Literals: numbers, <c>"text"</c> / <c>'text'</c>, <c>true</c>, <c>false</c>, <c>null</c></item>
/// <item>Variables (declared per engine) plus <c>user</c> (<c>Name</c>, <c>Id</c>, <c>Roles</c>, <c>IsInRole(r)</c>) and <c>now</c> (ISO UTC text)</item>
/// <item>Member access <c>a.b</c> and indexers <c>a["b"]</c>, <c>a[0]</c>: missing members and <c>null</c> targets yield <c>null</c>; <c>.Length</c>/<c>.Count</c></item>
/// <item>Operators: <c>! -</c>, <c>* / %</c>, <c>+ -</c> (<c>+</c> concatenates when one side is text), <c>&lt; &lt;= &gt; &gt;=</c>,
/// <c>== !=</c> (strict, no coercion), <c>&amp;&amp; ||</c> (truthiness, boolean result), <c>??</c>, <c>?:</c></item>
/// <item>Methods: text <c>Contains StartsWith EndsWith ToUpper ToLower Trim</c>, array <c>Contains</c>,
/// <c>Math.Round(x[, digits]) Abs Floor Ceiling Min Max</c> (rounding is half away from zero)</item>
/// <item>Numbers are doubles; division by zero and invalid operands yield <c>null</c> – evaluation never throws.</item>
/// </list>
/// Templates embed expressions in text: <c>"Order #{{ data.orderId }}"</c>. A template that is exactly one placeholder keeps the value's type.
/// </summary>
public sealed partial class PortableExpression
{
    private const int MaxCacheSize = 1000;
    private readonly HashSet<string> variables;
    private readonly ConcurrentDictionary<string, Node> cache = new(StringComparer.Ordinal);

    /// <param name="variables">Variables expressions may use (in addition to <c>user</c>, <c>now</c> and <c>Math</c>).</param>
    public PortableExpression(IEnumerable<string> variables)
    {
        this.variables = new HashSet<string>(variables, StringComparer.Ordinal) { "user", "now" };
    }

    public IReadOnlyCollection<string> Variables => variables;

    /// <summary>Parses <paramref name="expression"/>; returns the error message or <c>null</c> when it is valid.</summary>
    public string? Validate(string expression)
    {
        try
        {
            Parse(expression);
            return null;
        }
        catch (PortableExpressionException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Validates every <c>{{ expression }}</c> of a template value (objects and arrays recurse).</summary>
    public IEnumerable<string> ValidateTemplate(JsonNode? template) =>
        Placeholders(template).Select(Validate).OfType<string>();

    /// <summary>Evaluates to a runtime value (<c>null</c>, bool, double, string, JsonObject, JsonArray). Throws only on syntax errors.</summary>
    public object? Evaluate(string expression, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PortableEvaluator.Evaluate(Parse(expression), context);
    }

    public JsonNode? EvaluateToNode(string expression, ExpressionContext context) => PortableEvaluator.ToNode(Evaluate(expression, context));

    /// <summary>Truthiness of the result (null, false, 0 and "" are false).</summary>
    public bool EvaluateCondition(string expression, ExpressionContext context) => PortableEvaluator.IsTruthy(Evaluate(expression, context));

    /// <summary>Renders a template value: strings with placeholders are evaluated, objects and arrays recurse, other values are copied.</summary>
    public JsonNode? Render(JsonNode? template, ExpressionContext context) => template switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Render(p.Value, context)))),
        JsonArray array => new JsonArray([.. array.Select(item => Render(item, context))]),
        JsonValue value when value.TryGetValue<string>(out var text) => RenderString(text, context),
        _ => template?.DeepClone(),
    };

    /// <summary>Renders a text template to text.</summary>
    public string RenderText(string text, ExpressionContext context) => PortableEvaluator.ToText(PortableEvaluator.FromNode(RenderString(text, context)));

    /// <summary>Text of a runtime value as used in templates.</summary>
    public static string ToText(object? value) => PortableEvaluator.ToText(value);

    /// <summary>True when <paramref name="text"/> contains <c>{{ }}</c> placeholders.</summary>
    public static bool IsTemplate(string? text) => text is not null && PlaceholderRegex().IsMatch(text);

    private JsonNode? RenderString(string text, ExpressionContext context)
    {
        var matches = PlaceholderRegex().Matches(text);
        if (matches.Count == 0)
        {
            return JsonValue.Create(text);
        }

        if (matches.Count == 1 && matches[0].Length == text.Length)
        {
            return EvaluateToNode(matches[0].Groups[1].Value, context);
        }

        var sb = new StringBuilder();
        var last = 0;
        foreach (Match match in matches)
        {
            sb.Append(text, last, match.Index - last);
            sb.Append(PortableEvaluator.ToText(Evaluate(match.Groups[1].Value, context)));
            last = match.Index + match.Length;
        }

        sb.Append(text, last, text.Length - last);
        return JsonValue.Create(sb.ToString());
    }

    private Node Parse(string expression)
    {
        if (cache.TryGetValue(expression, out var node))
        {
            return node;
        }

        if (cache.Count >= MaxCacheSize)
        {
            cache.Clear();
        }

        return cache.GetOrAdd(expression, e => PortableParser.Parse(e, variables));
    }

    private static IEnumerable<string> Placeholders(JsonNode? template) => template switch
    {
        JsonObject obj => obj.SelectMany(p => Placeholders(p.Value)),
        JsonArray array => array.SelectMany(Placeholders),
        JsonValue value when value.TryGetValue<string>(out var text) => PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value),
        _ => [],
    };

    [GeneratedRegex(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PlaceholderRegex();
}
