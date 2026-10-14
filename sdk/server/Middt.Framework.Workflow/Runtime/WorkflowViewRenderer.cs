using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Renders the view of an instance's current state to HTML. Several renderers can be registered (Razor components,
/// JSON layouts, a template engine, ...); the first one that returns HTML wins, so the engine stays independent of any
/// UI technology.
/// </summary>
public interface IWorkflowViewRenderer
{
    /// <summary>Returns the HTML, or <c>null</c> when this renderer does not handle the state's view.</summary>
    Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser user, CancellationToken cancellationToken = default);
}

/// <summary>Static HTML of a state view (<c>GET …/instances/{id}/view</c>, e-mails, PDFs) through the registered <see cref="IWorkflowViewRenderer"/>s.</summary>
public sealed class WorkflowViewRenderer(IEnumerable<IWorkflowViewRenderer> renderers)
{
    /// <summary>Returns the HTML, or <c>null</c> when no renderer handles the state's view.</summary>
    public async Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser? user = null, CancellationToken cancellationToken = default)
    {
        foreach (var renderer in renderers)
        {
            if (await renderer.RenderAsync(instance, user ?? WorkflowUser.System, cancellationToken) is { } html)
            {
                return html;
            }
        }

        return null;
    }
}

/// <summary>
/// Renders <c>view.component</c> states with the components registered by <see cref="MiddtWorkflowOptions.UseViews"/>
/// (e.g. Razor views of Middt.Framework.Workflow.Razor) via <see cref="HtmlRenderer"/>. Output is encoded by Razor.
/// </summary>
internal sealed class ComponentWorkflowViewRenderer(IServiceProvider services, ILoggerFactory loggerFactory, IOptions<MiddtWorkflowOptions> options) : IWorkflowViewRenderer
{
    public async Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser user, CancellationToken cancellationToken = default)
    {
        if (instance.View is null || !options.Value.ViewTypes.TryGetValue(instance.View, out var viewType))
        {
            return null;
        }

        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Instance"] = instance,
                ["Interactive"] = false,
            });
            var output = await renderer.RenderComponentAsync(viewType, parameters);
            return output.ToHtmlString();
        });
    }
}
