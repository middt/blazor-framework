using Microsoft.AspNetCore.Components;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;

namespace Middt.Framework.Blazor.Web.Ui.Rendering;

/// <summary>Base class of widget components: the node (properties) and the render context.</summary>
public abstract class UiWidgetBase : ComponentBase
{
    [Parameter, EditorRequired]
    public UiNode Node { get; set; } = default!;

    [Parameter, EditorRequired]
    public UiRenderContext Context { get; set; } = default!;

    private bool parametersSet;

    /// <summary>
    /// Whether an update with the same <see cref="Node"/> and <see cref="Context"/> instances is ignored (default). Layout hosts
    /// keep both while nothing changed, so a re-render of a parent (e.g. a workflow screen) does not re-render (and reload)
    /// every grid and select. Interactive widgets still re-render on their own events. Override with <c>false</c> for a
    /// widget that renders state from outside its parameters.
    /// </summary>
    protected virtual bool SkipUnchangedParameters => true;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (parametersSet && SkipUnchangedParameters && IsUnchanged(parameters))
        {
            return Task.CompletedTask;
        }

        parametersSet = true;
        return base.SetParametersAsync(parameters);
    }

    private bool IsUnchanged(ParameterView parameters)
    {
        var count = 0;
        foreach (var parameter in parameters)
        {
            count++;
            var unchanged = !parameter.Cascading && parameter.Name switch
            {
                nameof(Node) => ReferenceEquals(parameter.Value, Node),
                nameof(Context) => ReferenceEquals(parameter.Value, Context),
                _ => false,
            };
            if (!unchanged)
            {
                return false;
            }
        }

        return count == 2;
    }

    /// <summary>Renders the string property <paramref name="name"/> as a template.</summary>
    protected string Text(string name, string? format = null) => Context.Text(Node.StringProp(name), format);

    private static long counter;
    private string? uniqueId;

    /// <summary>Element id: from the node id when set, otherwise unique per component instance (ids must not repeat on a page).</summary>
    protected string ElementId(string? suffix = null)
    {
        if (Node.Id is not null)
        {
            return Context.IdFor(Node, suffix);
        }

        uniqueId ??= $"{Context.IdPrefix}-{Node.Widget}{Interlocked.Increment(ref counter).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return suffix is null ? uniqueId : $"{uniqueId}-{suffix}";
    }
}
