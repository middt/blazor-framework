using System.Reflection;
using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Ui.Components.Display;
using Middt.Framework.Blazor.Ui.Widgets;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Blazor.Ui.Rendering;

/// <summary>Registers a Razor component as the Blazor implementation of a widget; it must derive from <see cref="UiWidgetBase"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UiWidgetAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// The Blazor renderer's widget implementations: widget name → Razor component. The specs live in the technology
/// independent <see cref="UiWidgetCatalog"/>; other renderers (Angular, static HTML) keep their own mapping.
/// </summary>
public sealed class UiBlazorWidgets
{
    private readonly Dictionary<string, Type> components = new(StringComparer.Ordinal);

    public UiBlazorWidgets(UiWidgetCatalog catalog)
    {
        Catalog = catalog;
    }

    public UiWidgetCatalog Catalog { get; }

    /// <summary>Built-in widgets: display components and the interactive <c>grid</c> and <c>record</c>.</summary>
    public static UiBlazorWidgets CreateDefault(UiWidgetCatalog? catalog = null) =>
        new UiBlazorWidgets(catalog ?? UiWidgetCatalog.CreateDefault())
            .Use<HeadingWidget>("heading")
            .Use<TextWidget>("text")
            .Use<FieldsWidget>("fields")
            .Use<AlertWidget>("alert")
            .Use<BadgeWidget>("badge")
            .Use<SectionWidget>("section")
            .Use<ColumnsWidget>("columns")
            .Use<DividerWidget>("divider")
            .Use<ListWidget>("list")
            .Use<LinkWidget>("link")
            .Use<UiGridWidget>("grid")
            .Use<UiRecordWidget>("record")
            .Use<UiLayoutWidget>("layout");

    /// <summary>Adds a custom widget: its spec (also register it on the API) and its component.</summary>
    public UiBlazorWidgets Add<TComponent>(UiWidgetSpec spec)
        where TComponent : UiWidgetBase
    {
        Catalog.Add(spec);
        return Use<TComponent>(spec.Name);
    }

    public UiBlazorWidgets Use<TComponent>(string name)
        where TComponent : UiWidgetBase =>
        Use(name, typeof(TComponent));

    public UiBlazorWidgets Use(string name, Type component)
    {
        if (Catalog.Find(name) is null)
        {
            throw new InvalidOperationException($"Widget '{name}' has no spec; add it to the catalog first.");
        }

        if (!typeof(UiWidgetBase).IsAssignableFrom(component))
        {
            throw new ArgumentException($"{component.Name} must derive from {nameof(UiWidgetBase)}.", nameof(component));
        }

        components[name] = component;
        return this;
    }

    /// <summary>Uses every <see cref="UiWidgetAttribute"/> component of the assembly of <typeparamref name="T"/> whose spec is registered.</summary>
    public UiBlazorWidgets UseComponentsFromAssemblyOf<T>()
    {
        foreach (var type in typeof(T).Assembly.GetTypes())
        {
            if (type.GetCustomAttribute<UiWidgetAttribute>() is { } attribute && typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract)
            {
                Use(attribute.Name, type);
            }
        }

        return this;
    }

    public Type? Find(string name) => components.GetValueOrDefault(name);
}
