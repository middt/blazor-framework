using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Renders the view of an instance's current state to HTML. Several renderers can be registered (JSON layouts,
/// a template engine, ...); the first one that returns HTML wins, so the engine stays independent of any
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
