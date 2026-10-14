using System.Collections.Concurrent;
using System.Dynamic;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace Middt.Framework.Workflow.Expressions;

/// <summary>
/// Evaluates C#-like expressions (DynamicExpresso) used in rules, mappings and task templates.
/// Expressions are compiled once and cached. Reflection and arbitrary type access are disabled: only the scope
/// variables, primitive types and <see cref="Math"/> are reachable.
/// </summary>
public sealed partial class WorkflowExpressionEngine
{
    private static readonly Parameter[] Parameters =
    [
        new("data", typeof(DynamicObject)),
        new("input", typeof(DynamicObject)),
        new("result", typeof(object)),
        new("child", typeof(DynamicObject)),
        new("user", typeof(WorkflowUser)),
        new("instance", typeof(DynamicObject)),
        new("now", typeof(DateTimeOffset)),
    ];

    private readonly Interpreter interpreter = new Interpreter(InterpreterOptions.Default)
        .Reference(typeof(Math))
        .Reference(typeof(TimeSpan));

    private readonly ConcurrentDictionary<string, Lambda> cache = new(StringComparer.Ordinal);

    /// <summary>Compiles <paramref name="expression"/> and returns the error message, or <c>null</c> when it is valid.</summary>
    public string? Validate(string expression)
    {
        try
        {
            Compile(expression);
            return null;
        }
        catch (Exception ex) when (ex is ParseException or InvalidOperationException or ArgumentException)
        {
            return ex.Message;
        }
    }

    /// <summary>Validates every <c>{{ expression }}</c> placeholder of a template value.</summary>
    public IEnumerable<string> ValidateTemplate(JsonNode? template) =>
        Placeholders(template).Select(Validate).OfType<string>();

    public object? Evaluate(string expression, ExpressionScope scope)
    {
        var lambda = Compile(expression);
        var result = lambda.Invoke(
            new JsonDynamic(scope.Data),
            new JsonDynamic(scope.Input),
            scope.Result is JsonObject obj ? new JsonDynamic(obj) : JsonDynamic.Unwrap(scope.Result),
            new JsonDynamic(scope.Child),
            scope.User,
            new JsonDynamic(scope.Instance),
            scope.Now);

        return result;
    }

    public bool EvaluateCondition(string expression, ExpressionScope scope) =>
        Evaluate(expression, scope) switch
        {
            bool b => b,
            null => false,
            var other => throw new InvalidOperationException($"Expression '{expression}' returned {other.GetType().Name}, expected bool."),
        };

    public JsonNode? EvaluateToNode(string expression, ExpressionScope scope) => JsonPath.ToNode(Evaluate(expression, scope));

    /// <summary>
    /// Renders a template: strings may contain <c>{{ expression }}</c> placeholders. A string that is exactly one placeholder
    /// keeps the expression's type (number, object, ...); otherwise placeholders are interpolated as text. Objects/arrays recurse.
    /// </summary>
    public JsonNode? Render(JsonNode? template, ExpressionScope scope)
    {
        switch (template)
        {
            case JsonObject obj:
                var renderedObject = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    renderedObject[key] = Render(value, scope);
                }

                return renderedObject;

            case JsonArray array:
                return new JsonArray([.. array.Select(item => Render(item, scope))]);

            case JsonValue value when value.TryGetValue<string>(out var text):
                return RenderString(text, scope);

            default:
                return template?.DeepClone();
        }
    }

    public string RenderText(string text, ExpressionScope scope) =>
        RenderString(text, scope) switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            null => string.Empty,
            var node => node.ToJsonString(),
        };

    private JsonNode? RenderString(string text, ExpressionScope scope)
    {
        var matches = PlaceholderRegex().Matches(text);
        if (matches.Count == 0)
        {
            return JsonValue.Create(text);
        }

        if (matches.Count == 1 && matches[0].Length == text.Length)
        {
            return EvaluateToNode(matches[0].Groups[1].Value, scope);
        }

        var sb = new StringBuilder();
        var last = 0;
        foreach (Match match in matches)
        {
            sb.Append(text, last, match.Index - last);
            sb.Append(System.Convert.ToString(Evaluate(match.Groups[1].Value, scope), System.Globalization.CultureInfo.InvariantCulture));
            last = match.Index + match.Length;
        }

        sb.Append(text, last, text.Length - last);
        return JsonValue.Create(sb.ToString());
    }

    private static IEnumerable<string> Placeholders(JsonNode? template) => template switch
    {
        JsonObject obj => obj.SelectMany(p => Placeholders(p.Value)),
        JsonArray array => array.SelectMany(Placeholders),
        JsonValue value when value.TryGetValue<string>(out var text) => PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value),
        _ => [],
    };

    private Lambda Compile(string expression) =>
        cache.GetOrAdd(expression, e => interpreter.Parse(e, Parameters));

    [GeneratedRegex(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
