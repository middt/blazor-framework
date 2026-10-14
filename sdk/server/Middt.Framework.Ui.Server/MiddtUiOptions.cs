using Microsoft.EntityFrameworkCore;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Server;

/// <summary>Configuration of <see cref="DependencyInjection.UiServiceCollectionExtensions.AddMiddtUi"/>.</summary>
public sealed class MiddtUiOptions
{
    /// <summary>Database schema of the layout tables (ignored by SQLite).</summary>
    public string? Schema { get; set; } = "ui";

    /// <summary>Creates the layout tables at startup when they do not exist (use EF migrations for production).</summary>
    public bool AutoCreateSchema { get; set; }

    /// <summary>Role required to create, change or delete layouts at runtime.</summary>
    public string AdminRole { get; set; } = "ui-admin";

    /// <summary>Maximum size of a layout document (characters).</summary>
    public int MaxLayoutLength { get; set; } = 256 * 1024;

    /// <summary>How long layouts are cached in memory (writes on this node invalidate immediately).</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Widgets layouts may use (built-ins plus custom specs added with <see cref="AddWidget"/>).</summary>
    public UiWidgetCatalog Widgets { get; } = UiWidgetCatalog.CreateDefault();

    internal Dictionary<string, UiHtmlWidget> HtmlWidgets { get; } = new(StringComparer.Ordinal);

    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    internal List<string> SeedDirectories { get; } = [];

    public MiddtUiOptions UseDbContext(Action<DbContextOptionsBuilder> configure)
    {
        ConfigureDbContext = configure;
        return this;
    }

    /// <summary>
    /// Seeds layouts from every <c>*.json</c> file of <paramref name="directory"/> (relative paths resolve against the content root):
    /// missing layouts are inserted, stored ones are replaced only when the file has a higher <c>version</c>, so runtime edits survive restarts.
    /// </summary>
    public MiddtUiOptions AddLayoutsFromDirectory(string directory)
    {
        SeedDirectories.Add(directory);
        return this;
    }

    /// <summary>
    /// Registers the spec of a custom widget so layouts using it are accepted. Each frontend registers its own
    /// implementation (Blazor component, Angular component); <paramref name="html"/> renders it for static HTML.
    /// </summary>
    public MiddtUiOptions AddWidget(UiWidgetSpec spec, UiHtmlWidget? html = null)
    {
        Widgets.Add(spec);
        if (html is not null)
        {
            HtmlWidgets[spec.Name] = html;
        }

        return this;
    }

    /// <summary>Sets the static HTML output of a widget (custom or built-in).</summary>
    public MiddtUiOptions AddHtmlWidget(string name, UiHtmlWidget html)
    {
        HtmlWidgets[name] = html;
        return this;
    }
}
