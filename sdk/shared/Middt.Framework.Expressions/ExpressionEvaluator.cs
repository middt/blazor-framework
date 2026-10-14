using System.Collections.Concurrent;
using System.Dynamic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace Middt.Framework.Expressions;

/// <summary>Variables of one evaluation: named JSON values plus the caller and the current time.</summary>
public sealed class ExpressionContext
{
    public Dictionary<string, JsonNode?> Variables { get; } = new(StringComparer.Ordinal);

    public ExpressionUser User { get; init; } = new() { Name = "system", IsSystem = true };

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public ExpressionContext Set(string name, JsonNode? value)
    {
        Variables[name] = value;
        return this;
    }
}

/// <summary>
/// Evaluates C#-like expressions (DynamicExpresso) over JSON variables, e.g. <c>data.amount &gt; 5000 &amp;&amp; user.IsInRole("manager")</c>,
/// and renders <c>{{ expression }}</c> templates. Expressions are compiled once and cached. Reflection and type access are
/// disabled: only the declared variables, <c>user</c>, <c>now</c>, primitive types and <see cref="Math"/> are reachable.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    private readonly Parameter[] parameters;
    private readonly string[] objectVariables;
    private readonly string[] valueVariables;
    private readonly Interpreter interpreter = new Interpreter(InterpreterOptions.Default)
        .Reference(typeof(Math))
        .Reference(typeof(TimeSpan));

    private readonly ConcurrentDictionary<string, Lambda> cache = new(StringComparer.Ordinal);

    /// <param name="objectVariables">Variables holding JSON objects (member access: <c>data.customer.name</c>; missing → null).</param>
    /// <param name="valueVariables">Variables that may hold any JSON value (object, array or primitive).</param>
    public ExpressionEvaluator(IEnumerable<string> objectVariables, IEnumerable<string>? valueVariables = null)
    {
        this.objectVariables = [.. objectVariables];
        this.valueVariables = [.. valueVariables ?? []];
        parameters =
        [
            .. this.objectVariables.Select(v => new Parameter(v, typeof(DynamicObject))),
            .. this.valueVariables.Select(v => new Parameter(v, typeof(object))),
            new Parameter("user", typeof(ExpressionUser)),
            new Parameter("now", typeof(DateTimeOffset)),
        ];
    }

    public IReadOnlyList<string> VariableNames => [.. objectVariables, .. valueVariables, "user", "now"];

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

    public object? Evaluate(string expression, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var lambda = Compile(expression);

        var args = new object?[parameters.Length];
        var i = 0;
        foreach (var name in objectVariables)
        {
            args[i++] = new JsonDynamic(context.Variables.GetValueOrDefault(name) as JsonObject);
        }

        foreach (var name in valueVariables)
        {
            var value = context.Variables.GetValueOrDefault(name);
            args[i++] = value is JsonObject obj ? new JsonDynamic(obj) : JsonDynamic.Unwrap(value);
        }

        args[i++] = context.User;
        args[i] = context.Now;
        return lambda.Invoke(args);
    }

    public bool EvaluateCondition(string expression, ExpressionContext context) =>
        Evaluate(expression, context) switch
        {
            bool b => b,
            null => false,
            var other => throw new InvalidOperationException($"Expression '{expression}' returned {other.GetType().Name}, expected bool."),
        };

    public JsonNode? EvaluateToNode(string expression, ExpressionContext context) => JsonPath.ToNode(Evaluate(expression, context));

    /// <summary>
    /// Renders a template: strings may contain <c>{{ expression }}</c> placeholders. A string that is exactly one placeholder
    /// keeps the expression's type (number, object, ...); otherwise placeholders are interpolated as text. Objects/arrays recurse.
    /// </summary>
    public JsonNode? Render(JsonNode? template, ExpressionContext context)
    {
        switch (template)
        {
            case JsonObject obj:
                var renderedObject = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    renderedObject[key] = Render(value, context);
                }

                return renderedObject;

            case JsonArray array:
                return new JsonArray([.. array.Select(item => Render(item, context))]);

            case JsonValue value when value.TryGetValue<string>(out var text):
                return RenderString(text, context);

            default:
                return template?.DeepClone();
        }
    }

    public string RenderText(string text, ExpressionContext context) =>
        RenderString(text, context) switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            null => string.Empty,
            var node => node is JsonValue ? node.ToString() : node.ToJsonString(),
        };

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
            sb.Append(Convert.ToString(Evaluate(match.Groups[1].Value, context), CultureInfo.InvariantCulture));
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
        cache.GetOrAdd(expression, e => interpreter.Parse(e, parameters));

    [GeneratedRegex(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
