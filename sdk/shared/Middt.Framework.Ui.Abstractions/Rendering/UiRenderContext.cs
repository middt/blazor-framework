using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Expressions.Portable;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Rendering;

/// <summary>Expression variables of layouts.</summary>
public static class UiExpressions
{
    /// <summary>Page/view data (e.g. the workflow instance data).</summary>
    public const string Data = "data";

    /// <summary>Query string values of the page.</summary>
    public const string Query = "query";

    /// <summary>Current item of a grid/list.</summary>
    public const string Row = "row";

    /// <summary>Record loaded by a <c>record</c> widget.</summary>
    public const string Record = "record";

    /// <summary>Workflow instance (<c>instance.state</c>, <c>instance.key</c>, ...).</summary>
    public const string Instance = "instance";

    public static IReadOnlyList<string> Variables { get; } = [Data, Query, Row, Record, Instance];

    /// <summary>
    /// Shared evaluator of the <see cref="PortableExpression">portable expression language</see> (parsed expressions are
    /// cached). Layouts use it – not C# – so every renderer (Blazor, Angular, static HTML) evaluates them identically.
    /// </summary>
    public static PortableExpression Evaluator { get; } = new(Variables);
}

/// <summary>What a widget renders with: the variables, the caller, the catalog and whether the host is interactive.</summary>
public sealed partial class UiRenderContext
{
    private readonly Dictionary<string, JsonNode?> variables;

    public UiRenderContext(UiWidgetCatalog catalog, ExpressionUser user, IReadOnlyDictionary<string, JsonNode?>? variables = null, bool interactive = false)
    {
        Catalog = catalog;
        User = user;
        Interactive = interactive;
        this.variables = variables is null ? new(StringComparer.Ordinal) : new(variables, StringComparer.Ordinal);
    }

    private UiRenderContext(UiRenderContext parent, Dictionary<string, JsonNode?> variables)
    {
        Catalog = parent.Catalog;
        User = parent.User;
        Interactive = parent.Interactive;
        Now = parent.Now;
        IdPrefix = parent.IdPrefix;
        Culture = parent.Culture;
        this.variables = variables;
    }

    public UiWidgetCatalog Catalog { get; }

    public ExpressionUser User { get; }

    /// <summary><c>false</c> for static rendering (HtmlRenderer): interactive widgets render a placeholder.</summary>
    public bool Interactive { get; }

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Prefix of generated element ids.</summary>
    public string IdPrefix { get; init; } = "ui";

    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    public IReadOnlyDictionary<string, JsonNode?> Variables => variables;

    public JsonNode? this[string name] => variables.GetValueOrDefault(name);

    /// <summary>Copy with one more (or a replaced) variable, e.g. <c>row</c> for list items.</summary>
    public UiRenderContext With(string name, JsonNode? value) =>
        new(this, new Dictionary<string, JsonNode?>(variables, StringComparer.Ordinal) { [name] = value });

    public ExpressionContext ToExpressionContext()
    {
        var context = new ExpressionContext { User = User, Now = Now };
        foreach (var (name, value) in variables)
        {
            context.Set(name, value);
        }

        return context;
    }

    public object? Evaluate(string expression) => UiExpressions.Evaluator.Evaluate(expression, ToExpressionContext());

    /// <summary>Renders a template value (objects/arrays recurse); see <see cref="PortableExpression.Render"/>.</summary>
    public JsonNode? Render(JsonNode? template) => UiExpressions.Evaluator.Render(template, ToExpressionContext());

    /// <summary>Renders a text template; expression errors are returned as text so layout authors can see them.</summary>
    public string Text(string? template, string? format = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        if (!PortableExpression.IsTemplate(template))
        {
            return template;
        }

        try
        {
            return Format(UiExpressions.Evaluator.Render(JsonValue.Create(template), ToExpressionContext()), format);
        }
        catch (PortableExpressionException ex)
        {
            return $"⚠ {ex.Message}";
        }
    }

    /// <summary>
    /// Formats a value with a <see cref="UiFormats">portable format</see>: numbers <c>N0</c>–<c>N6</c> / <c>F0</c>–<c>F6</c>,
    /// ISO dates <c>d</c>, <c>D</c>, <c>g</c>, <c>t</c> (in <see cref="Culture"/>); booleans as Evet/Hayır.
    /// </summary>
    public string Format(JsonNode? value, string? format = null)
    {
        if (format is not null && !UiFormats.IsValid(format))
        {
            format = null;
        }

        switch (value)
        {
            case null:
                return string.Empty;
            // Number formats apply to numbers, date formats to ISO 8601 date strings – nothing else (same rules as @middt/ui-core).
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                // Computed values are JsonValue<long/double>: TryGetValue<decimal> only succeeds for parsed JSON or decimals.
                return v.TryGetValue<decimal>(out var number) || decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    ? number.ToString(UiFormats.IsNumber(format) ? format : null, Culture)
                    : v.ToJsonString();
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var text = v.GetValue<string>();
                return format is { Length: > 0 } && !UiFormats.IsNumber(format) && IsoDateRegex().IsMatch(text)
                    && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                    ? (date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date).ToString(format, Culture)
                    : text;
            case JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                return v.GetValue<bool>() ? "Evet" : "Hayır";
            default:
                return PortableExpression.ToText(value); // JSON text like JSON.stringify (no escaping of non-ASCII)
        }
    }

    /// <summary>Evaluates <see cref="UiNode.VisibleWhen"/>; a failing expression hides the node.</summary>
    public bool IsVisible(UiNode node) => IsVisible(node.VisibleWhen);

    public bool IsVisible(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        try
        {
            return UiExpressions.Evaluator.EvaluateCondition(condition, ToExpressionContext());
        }
        catch (PortableExpressionException)
        {
            return false;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex IsoDateRegex();

    public string IdFor(UiNode node, string? suffix = null) =>
        $"{IdPrefix}-{node.Id ?? node.Widget}{(suffix is null ? null : "-" + suffix)}";

}

/// <summary>Value formats every renderer supports (the TypeScript client implements the same set with Intl).</summary>
public static partial class UiFormats
{
    public static IReadOnlyList<string> Dates { get; } = ["d", "D", "g", "t"];

    /// <summary><c>N0</c>–<c>N6</c> (grouped), <c>F0</c>–<c>F6</c> (fixed) and the date formats.</summary>
    public static bool IsValid(string? format) => format is not null && (Dates.Contains(format) || NumberRegex().IsMatch(format));

    /// <summary>True for the number formats (N0–N6, F0–F6).</summary>
    public static bool IsNumber(string? format) => format is not null && NumberRegex().IsMatch(format);

    [System.Text.RegularExpressions.GeneratedRegex("^[NF][0-6]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex NumberRegex();
}

/// <summary>
/// Link safety: only local paths and http(s) URLs are rendered as links. Control characters and whitespace are rejected
/// anywhere, because browsers strip them while parsing (<c>"/\t/evil.example"</c> becomes the off-site <c>//evil.example</c>).
/// The only exception is a plain space inside the URL (not leading/trailing): browsers percent-encode it instead of
/// removing it, and it occurs in unencoded query values (<c>/orders?q=a b</c>).
/// </summary>
public static class UiUrl
{
    public static bool IsSafe(string? url) =>
        url is { Length: > 0 }
        && !HasUnsafeCharacters(url)
        && (IsLocalPath(url)
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)));

    /// <summary>True for local paths only (used for navigation from grid actions).</summary>
    public static bool IsLocal(string? url) => url is { Length: > 0 } && !HasUnsafeCharacters(url) && IsLocalPath(url);

    // "/path" but not the protocol relative "//host" or "/\host".
    private static bool IsLocalPath(string url) => url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    private static bool HasUnsafeCharacters(string url)
    {
        if (url[0] == ' ' || url[^1] == ' ')
        {
            return true;
        }

        foreach (var c in url)
        {
            if (char.IsControl(c) || (char.IsWhiteSpace(c) && c != ' '))
            {
                return true;
            }
        }

        return false;
    }
}
