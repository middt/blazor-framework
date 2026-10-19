using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Middt.Framework.Ui.Model;

namespace Middt.Framework.Ui.Widgets;

/// <summary>Specs of the built-in widgets.</summary>
public static partial class UiWidgets
{
    public static readonly IReadOnlyList<string> FieldTypes = ["text", "textarea", "number", "integer", "date", "datetime", "checkbox", "select", "picker"];

    private static readonly UiPropSpec[] ColumnProps =
    [
        new("field", UiPropType.String, "Property of the row (camelCase).") { Required = true },
        new("title", UiPropType.String),
        new("format", UiPropType.Format, "N0–N6, F0–F6 (numbers) or d, D, g, t (dates)."),
        new("width", UiPropType.String, "CSS width, e.g. 120px."),
        new("sortable", UiPropType.Boolean, "Default true."),
        new("template", UiPropType.Template, "Cell text, e.g. {{ row.firstName + \" \" + row.lastName }}."),
    ];

    private static readonly UiPropSpec[] LookupProps =
    [
        new("entity", UiPropType.String, "Entity (controller) the options are loaded from.") { Required = true },
        new("valueField", UiPropType.String) { Required = true },
        new("textField", UiPropType.String) { Required = true },
        new("title", UiPropType.String, "Picker dialog title."),
        new("columns", UiPropType.Array, "Picker dialog columns.") { Properties = ColumnProps },
        new("search", UiPropType.Array, "Picker dialog search fields.") { Properties = BasicFieldProps() },
    ];

    /// <summary>Properties of a form/search field.</summary>
    public static readonly UiPropSpec[] FieldProps =
    [
        .. BasicFieldProps(),
        new("lookup", UiPropType.Object, "Options (select) or dialog (picker) loaded from an entity.") { Properties = LookupProps },
    ];

    private static readonly UiPropSpec[] ActionProps =
    [
        new("text", UiPropType.String),
        new("icon", UiPropType.String, "Material icon name."),
        new("navigate", UiPropType.Template, "Local URL, e.g. /ui/bike-category-products?id={{ row.categoryId }}.") { Required = true },
        new("style", UiPropType.Enum) { Values = ["primary", "secondary", "light", "danger"] },
    ];

    public static UiWidgetSpec Heading { get; } = new("heading", "Title text.")
    {
        Props = [new("text", UiPropType.Template) { Required = true }, new("level", UiPropType.Integer, "1-6, default 2.")],
        Validate = (node, path, errors) =>
        {
            if (node.Prop("level") is not null && node.IntProp("level") is < 1 or > 6)
            {
                errors.Add($"{path}.level: must be between 1 and 6.");
            }
        },
    };

    public static UiWidgetSpec Text { get; } = new("text", "Paragraph.")
    {
        Props = [new("text", UiPropType.Template) { Required = true }, new("style", UiPropType.Enum) { Values = ["normal", "muted", "lead", "small", "code"] }],
    };

    public static UiWidgetSpec Fields { get; } = new("fields", "Label/value list.")
    {
        Props =
        [
            new("items", UiPropType.Array)
            {
                Required = true,
                Properties =
                [
                    new("label", UiPropType.String) { Required = true },
                    new("value", UiPropType.Template) { Required = true },
                    new("format", UiPropType.Format),
                    new("visibleWhen", UiPropType.Expression),
                ],
            },
        ],
    };

    public static UiWidgetSpec Alert { get; } = new("alert", "Highlighted message.")
    {
        Props = [new("text", UiPropType.Template) { Required = true }, new("severity", UiPropType.Enum) { Values = ["info", "success", "warning", "danger"] }],
    };

    public static UiWidgetSpec Badge { get; } = new("badge", "Small status label.")
    {
        Props = [new("text", UiPropType.Template) { Required = true }, new("color", UiPropType.Enum) { Values = ["primary", "secondary", "success", "warning", "danger", "info"] }],
    };

    public static UiWidgetSpec Section { get; } = new("section", "Card with an optional title.")
    {
        Children = UiChildren.Required,
        Props = [new("title", UiPropType.Template)],
    };

    public static UiWidgetSpec Columns { get; } = new("columns", "Shows its children side by side (one column each, stacked on small screens).")
    {
        Children = UiChildren.Required,
        Props = [new("widths", UiPropType.Options, "Relative widths, e.g. [\"2\", \"1\"].")],
    };

    public static UiWidgetSpec Divider { get; } = new("divider", "Horizontal line.");

    public static UiWidgetSpec List { get; } = new("list", "Repeats its children for each item of an array (the item is 'row').")
    {
        Children = UiChildren.Required,
        Props = [new("source", UiPropType.Expression, "Expression returning an array, e.g. data.items.") { Required = true }, new("empty", UiPropType.String, "Text shown when the array is empty.")],
    };

    public static UiWidgetSpec Link { get; } = new("link", "Link to a local page (or http/https URL).")
    {
        Props =
        [
            new("text", UiPropType.Template) { Required = true },
            new("href", UiPropType.Template) { Required = true },
            new("style", UiPropType.Enum) { Values = ["link", "button"] },
        ],
    };

    public static UiWidgetSpec Grid { get; } = new("grid", "Server paged data grid of an entity with search, row actions and optional create/edit/delete.")
    {
        Interactive = true,
        Props =
        [
            new("entity", UiPropType.String, "Controller name, e.g. Category (api/v{version}/{entity}/getitems).") { Required = true },
            new("title", UiPropType.Template),
            new("columns", UiPropType.Array) { Required = true, Properties = ColumnProps },
            new("search", UiPropType.Array, "Search fields (filter object properties).") { Properties = FieldProps },
            new("filter", UiPropType.TemplateMap, "Fixed filter, e.g. { \"categoryId\": \"{{ query.id }}\" }."),
            new("pageSize", UiPropType.Integer, "Default 10."),
            new("autoLoad", UiPropType.Boolean, "Search when the page opens (default true)."),
            new("create", UiPropType.Boolean),
            new("edit", UiPropType.Boolean),
            new("delete", UiPropType.Boolean),
            new("form", UiPropType.Array, "Create/edit form fields.") { Properties = FieldProps },
            new("formTitle", UiPropType.String),
            new("rowActions", UiPropType.Array) { Properties = ActionProps },
        ],
        Validate = (node, path, errors) =>
        {
            ValidateEntity(node.StringProp("entity"), $"{path}.entity", errors);
            if ((node.BoolProp("create") || node.BoolProp("edit")) && node.ArrayProp("form") is not { Count: > 0 })
            {
                errors.Add($"{path}.form: required when create or edit is enabled.");
            }

            if (node.Prop("pageSize") is not null && node.IntProp("pageSize") is < 1 or > 500)
            {
                errors.Add($"{path}.pageSize: must be between 1 and 500.");
            }

            ValidateFieldNames(node.ArrayProp("columns"), "field", $"{path}.columns", errors);
            ValidateFields(node.ArrayProp("search"), $"{path}.search", errors);
            ValidateFields(node.ArrayProp("form"), $"{path}.form", errors);
            if (node.ObjectProp("filter") is { } filter)
            {
                foreach (var (name, _) in filter)
                {
                    if (!FieldNameRegex().IsMatch(name))
                    {
                        errors.Add($"{path}.filter.{name}: invalid field name.");
                    }
                }
            }
        },
    };

    public static UiWidgetSpec Record { get; } = new("record", "Loads one entity by id and shows its children with it as 'record'.")
    {
        Interactive = true,
        Children = UiChildren.Required,
        Props =
        [
            new("entity", UiPropType.String) { Required = true },
            new("recordId", UiPropType.Template, "Id of the record, e.g. {{ query.id }}.") { Required = true },
            new("notFound", UiPropType.String, "Text shown when the record does not exist."),
        ],
        Validate = (node, path, errors) => ValidateEntity(node.StringProp("entity"), $"{path}.entity", errors),
    };

    public static UiWidgetSpec Layout { get; } = new("layout", "Embeds a reusable view layout by key (optionally scoping 'data' to a sub-expression).")
    {
        Interactive = true,
        Props =
        [
            new("key", UiPropType.String, "Key of the reusable view layout (kind 'view'), e.g. workflow-footer.") { Required = true },
            new("data", UiPropType.Expression, "Optional expression whose value becomes 'data' inside the embedded layout, e.g. data.customer."),
            new("title", UiPropType.Template, "Optional label shown in static placeholders."),
        ],
        Validate = (node, path, errors) =>
        {
            if (node.StringProp("key") is { } key && !Validation.UiLayoutValidator.IsValidKey(key))
            {
                errors.Add($"{path}.key: must be a valid layout key (lowercase letters, digits, '-', '_', '.').");
            }
        },
    };

    public static IReadOnlyList<UiWidgetSpec> All { get; } = [Heading, Text, Fields, Alert, Badge, Section, Columns, Divider, List, Link, Grid, Record, Layout];

    private static UiPropSpec[] BasicFieldProps() =>
    [
        new("field", UiPropType.String, "Property name (camelCase).") { Required = true },
        new("label", UiPropType.String),
        new("type", UiPropType.Enum, "Default text.") { Values = FieldTypes },
        new("required", UiPropType.Boolean),
        new("min", UiPropType.Number),
        new("max", UiPropType.Number),
        new("maxLength", UiPropType.Integer),
        new("placeholder", UiPropType.String),
        new("options", UiPropType.Options, "Static select options: strings or { value, text }.") { Properties = [new("value", UiPropType.String) { Required = true }, new("text", UiPropType.String)] },
    ];

    internal static void ValidateEntity(string? entity, string path, ICollection<string> errors)
    {
        if (entity is not null && !EntityRegex().IsMatch(entity))
        {
            errors.Add($"{path}: must be a controller name (letters and digits).");
        }
    }

    private static void ValidateFieldNames(JsonArray? items, string property, string path, ICollection<string> errors)
    {
        for (var i = 0; i < (items?.Count ?? 0); i++)
        {
            if (items![i] is JsonObject item && item[property] is JsonValue value && value.TryGetValue<string>(out var name) && !FieldNameRegex().IsMatch(name))
            {
                errors.Add($"{path}[{i}].{property}: invalid field name '{name}'.");
            }
        }
    }

    private static void ValidateFields(JsonArray? fields, string path, ICollection<string> errors)
    {
        ValidateFieldNames(fields, "field", path, errors);
        for (var i = 0; i < (fields?.Count ?? 0); i++)
        {
            if (fields![i] is not JsonObject field)
            {
                continue;
            }

            var type = field["type"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : "text";
            if (type == "picker" && field["lookup"] is null)
            {
                errors.Add($"{path}[{i}].lookup: required for picker fields.");
            }

            if (type == "select" && field["lookup"] is null && field["options"] is null)
            {
                errors.Add($"{path}[{i}]: select fields need options or lookup.");
            }

            if (field["lookup"] is JsonObject lookup)
            {
                ValidateEntity(lookup["entity"] is JsonValue e && e.TryGetValue<string>(out var entity) ? entity : null, $"{path}[{i}].lookup.entity", errors);
                if (lookup["valueField"] is JsonValue vf && vf.TryGetValue<string>(out var valueField) && !FieldNameRegex().IsMatch(valueField))
                {
                    errors.Add($"{path}[{i}].lookup.valueField: invalid field name '{valueField}'.");
                }

                if (lookup["textField"] is JsonValue tf && tf.TryGetValue<string>(out var textField) && !FieldNameRegex().IsMatch(textField))
                {
                    errors.Add($"{path}[{i}].lookup.textField: invalid field name '{textField}'.");
                }

                ValidateFieldNames(lookup["columns"] as JsonArray, "field", $"{path}[{i}].lookup.columns", errors);
                ValidateFields(lookup["search"] as JsonArray, $"{path}[{i}].lookup.search", errors);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    internal static partial Regex FieldNameRegex();
}
