namespace Middt.Framework.Ui.Widgets;

#pragma warning disable CA1720 // names mirror JSON types
public enum UiPropType
{
    /// <summary>Plain text.</summary>
    String,

    /// <summary>Text with <c>{{ expression }}</c> placeholders.</summary>
    Template,

    /// <summary>A C#-like expression (no braces).</summary>
    Expression,
    Number,
    Integer,
    Boolean,

    /// <summary>One of <see cref="UiPropSpec.Values"/>.</summary>
    Enum,

    /// <summary>Object with the properties in <see cref="UiPropSpec.Properties"/>.</summary>
    Object,

    /// <summary>Array of objects with the properties in <see cref="UiPropSpec.Properties"/>.</summary>
    Array,

    /// <summary>Object with arbitrary keys whose values are templates (e.g. a fixed filter).</summary>
    TemplateMap,

    /// <summary>Array of strings, or of objects with the properties in <see cref="UiPropSpec.Properties"/>.</summary>
    Options,

    /// <summary>A portable value format (<see cref="Rendering.UiFormats"/>): N0–N6, F0–F6, d, D, g, t.</summary>
    Format,
}
#pragma warning restore CA1720

/// <summary>Describes one widget property (used by the validator and the layout editor).</summary>
public sealed class UiPropSpec(string name, UiPropType type, string? description = null)
{
    public string Name { get; } = name;

    public UiPropType Type { get; } = type;

    public string? Description { get; } = description;

    public bool Required { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>Properties of an <see cref="UiPropType.Object"/> or of the items of an <see cref="UiPropType.Array"/>.</summary>
    public IReadOnlyList<UiPropSpec> Properties { get; init; } = [];
}

public enum UiChildren
{
    None,
    Optional,
    Required,
}

/// <summary>Describes a widget: its properties, whether it has children and whether it needs an interactive (Blazor) host.</summary>
public sealed class UiWidgetSpec(string name, string description)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public UiChildren Children { get; init; }

    /// <summary>Interactive widgets (data grids, forms) need a Blazor circuit; static rendering shows a placeholder.</summary>
    public bool Interactive { get; init; }

    public IReadOnlyList<UiPropSpec> Props { get; init; } = [];

    /// <summary>Extra validation beyond the property specs: <c>(node, path, errors)</c>.</summary>
    public Action<Model.UiNode, string, ICollection<string>>? Validate { get; init; }
}
