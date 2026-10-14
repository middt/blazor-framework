namespace Middt.Framework.Ui.Widgets;

/// <summary>
/// The widgets a layout may use – their <see cref="UiWidgetSpec">specs</see> only (validation, editors, JSON Schema).
/// The catalog is independent of any UI technology: each renderer (Blazor, Angular, static HTML) maps widget names to its
/// own implementation. API and frontends must register the same custom specs.
/// </summary>
public sealed class UiWidgetCatalog
{
    private readonly Dictionary<string, UiWidgetSpec> specs = new(StringComparer.Ordinal);

    /// <summary>Catalog with the built-in widgets (<see cref="UiWidgets.All"/>).</summary>
    public static UiWidgetCatalog CreateDefault()
    {
        var catalog = new UiWidgetCatalog();
        foreach (var spec in UiWidgets.All)
        {
            catalog.Add(spec);
        }

        return catalog;
    }

    public IReadOnlyCollection<UiWidgetSpec> Specs => specs.Values;

    /// <summary>Adds (or replaces) a widget spec.</summary>
    public UiWidgetCatalog Add(UiWidgetSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        specs[spec.Name] = spec;
        return this;
    }

    public UiWidgetSpec? Find(string name) => specs.GetValueOrDefault(name);
}
