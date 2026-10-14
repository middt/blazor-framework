using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Validation;

/// <summary>
/// Validates layouts against the widget catalog: known widgets, known and well typed properties, compilable expressions
/// and templates, children only where allowed, unique ids and size limits. Errors carry a path, e.g. <c>body[1].columns[0].field</c>.
/// </summary>
public sealed partial class UiLayoutValidator(UiWidgetCatalog catalog)
{
    public const int MaxNodes = 500;
    public const int MaxDepth = 24;

    private static readonly HashSet<string> Kinds = [UiLayoutKind.Page, UiLayoutKind.View];

    public UiValidationResult Validate(string json)
    {
        try
        {
            return Validate(UiJson.Parse(json));
        }
        catch (JsonException ex)
        {
            return new UiValidationResult { Errors = [$"Invalid JSON: {ex.Message}"] };
        }
    }

    public UiValidationResult Validate(UiLayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var errors = new List<string>();

        if (!IsValidKey(layout.Key))
        {
            errors.Add("key: required; lowercase letters, digits, '-', '_' or '.', at most 100 characters.");
        }

        if (!Kinds.Contains(layout.Kind))
        {
            errors.Add($"kind: must be '{UiLayoutKind.Page}' or '{UiLayoutKind.View}'.");
        }

        if (layout.Version < 0)
        {
            errors.Add("version: must not be negative.");
        }

        if (layout.Menu is not null && layout.Kind != UiLayoutKind.Page)
        {
            errors.Add("menu: only pages can have a menu entry.");
        }

        if (layout.Roles.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("roles: must not contain empty values.");
        }

        var state = new State(errors);
        ValidateNodes(layout.Body, "body", 1, state);
        return new UiValidationResult { Errors = errors };
    }

    public static bool IsValidKey(string? key) => key is not null && KeyRegex().IsMatch(key);

    private void ValidateNodes(List<UiNode>? nodes, string path, int depth, State state)
    {
        if (nodes is null)
        {
            return;
        }

        if (depth > MaxDepth)
        {
            state.Errors.Add($"{path}: nesting is deeper than {MaxDepth} levels.");
            return;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            if (++state.Count > MaxNodes)
            {
                state.Errors.Add($"Layout has more than {MaxNodes} widgets.");
                return;
            }

            ValidateNode(nodes[i], $"{path}[{i}]", depth, state);
        }
    }

    private void ValidateNode(UiNode? node, string path, int depth, State state)
    {
        var errors = state.Errors;
        if (node is null)
        {
            errors.Add($"{path}: must be an object.");
            return;
        }

        if (node.Id is not null && (!UiWidgets.FieldNameRegex().IsMatch(node.Id) || !state.Ids.Add(node.Id)))
        {
            errors.Add($"{path}.id: must be a unique identifier.");
        }

        if (node.VisibleWhen is not null)
        {
            ValidateExpression(node.VisibleWhen, $"{path}.visibleWhen", errors);
        }

        if (string.IsNullOrEmpty(node.Widget))
        {
            errors.Add($"{path}.widget: required.");
            return;
        }

        var spec = catalog.Find(node.Widget);
        if (spec is null)
        {
            errors.Add($"{path}.widget: unknown widget '{node.Widget}'.");
            return;
        }

        path = $"{path}({node.Widget})";
        ValidateProps(node.Props, spec.Props, path, errors);
        spec.Validate?.Invoke(node, path, errors);

        switch (spec.Children)
        {
            case UiChildren.None when node.Children is { Count: > 0 }:
                errors.Add($"{path}.children: '{node.Widget}' has no children.");
                break;
            case UiChildren.Required when node.Children is not { Count: > 0 }:
                errors.Add($"{path}.children: '{node.Widget}' needs at least one child.");
                break;
        }

        ValidateNodes(node.Children, $"{path}.children", depth + 1, state);
    }

    private static void ValidateProps(JsonObject? values, IReadOnlyList<UiPropSpec> specs, string path, ICollection<string> errors)
    {
        foreach (var spec in specs)
        {
            if (spec.Required && values?[spec.Name] is null)
            {
                errors.Add($"{path}.{spec.Name}: required.");
            }
        }

        if (values is null)
        {
            return;
        }

        foreach (var (name, value) in values)
        {
            var spec = specs.FirstOrDefault(s => s.Name == name);
            if (spec is null)
            {
                errors.Add($"{path}.{name}: unknown property.");
            }
            else if (value is not null)
            {
                ValidateValue(value, spec, $"{path}.{name}", errors);
            }
        }
    }

    private static void ValidateValue(JsonNode value, UiPropSpec spec, string path, ICollection<string> errors)
    {
        var kind = value.GetValueKind();
        switch (spec.Type)
        {
            case UiPropType.String or UiPropType.Template or UiPropType.Expression or UiPropType.Enum or UiPropType.Format when kind != JsonValueKind.String:
                errors.Add($"{path}: must be a string.");
                return;
            case UiPropType.Template:
                ValidateTemplate(value, path, errors);
                return;
            case UiPropType.Expression:
                ValidateExpression(value.GetValue<string>(), path, errors);
                return;
            case UiPropType.Format when !UiFormats.IsValid(value.GetValue<string>()):
                errors.Add($"{path}: unsupported format; use N0–N6, F0–F6, d, D, g or t.");
                return;
            case UiPropType.Enum when !spec.Values.Contains(value.GetValue<string>(), StringComparer.Ordinal):
                errors.Add($"{path}: must be one of {string.Join(", ", spec.Values)}.");
                return;
            case UiPropType.Number when kind != JsonValueKind.Number:
                errors.Add($"{path}: must be a number.");
                return;
            case UiPropType.Integer when kind != JsonValueKind.Number || !value.AsValue().TryGetValue<int>(out _):
                errors.Add($"{path}: must be an integer.");
                return;
            case UiPropType.Boolean when kind is not (JsonValueKind.True or JsonValueKind.False):
                errors.Add($"{path}: must be true or false.");
                return;
            case UiPropType.Object when value is not JsonObject:
            case UiPropType.TemplateMap when value is not JsonObject:
                errors.Add($"{path}: must be an object.");
                return;
            case UiPropType.Object:
                ValidateProps((JsonObject)value, spec.Properties, path, errors);
                return;
            case UiPropType.TemplateMap:
                foreach (var (key, item) in (JsonObject)value)
                {
                    ValidateTemplate(item, $"{path}.{key}", errors);
                }

                return;
            case UiPropType.Array or UiPropType.Options when value is not JsonArray:
                errors.Add($"{path}: must be an array.");
                return;
            case UiPropType.Array:
                var items = (JsonArray)value;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is JsonObject item)
                    {
                        ValidateProps(item, spec.Properties, $"{path}[{i}]", errors);
                    }
                    else
                    {
                        errors.Add($"{path}[{i}]: must be an object.");
                    }
                }

                return;
            case UiPropType.Options:
                var options = (JsonArray)value;
                for (var i = 0; i < options.Count; i++)
                {
                    switch (options[i])
                    {
                        case JsonObject option when spec.Properties.Count > 0:
                            ValidateProps(option, spec.Properties, $"{path}[{i}]", errors);
                            break;
                        case JsonValue option when option.GetValueKind() is JsonValueKind.String or JsonValueKind.Number:
                            break;
                        default:
                            errors.Add($"{path}[{i}]: must be a string{(spec.Properties.Count > 0 ? " or an object" : string.Empty)}.");
                            break;
                    }
                }

                return;
        }
    }

    private static void ValidateTemplate(JsonNode? template, string path, ICollection<string> errors)
    {
        foreach (var error in UiExpressions.Evaluator.ValidateTemplate(template))
        {
            errors.Add($"{path}: {error}");
        }
    }

    private static void ValidateExpression(string expression, string path, ICollection<string> errors)
    {
        if (UiExpressions.Evaluator.Validate(expression) is { } error)
        {
            errors.Add($"{path}: {error}");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    private sealed class State(List<string> errors)
    {
        public List<string> Errors { get; } = errors;

        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

        public int Count { get; set; }
    }
}
