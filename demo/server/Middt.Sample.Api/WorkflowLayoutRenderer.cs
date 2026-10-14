using System.Text.Json.Nodes;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Server.Runtime;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Sample.Api;

/// <summary>Renders <c>view.layout</c> states with the UI module's static HTML renderer (<c>GET …/instances/{id}/view</c>, e-mails).</summary>
public sealed class WorkflowLayoutRenderer(UiLayoutStore store, UiHtmlRenderer renderer) : IWorkflowViewRenderer
{
    public async Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser user, CancellationToken cancellationToken = default)
    {
        if (instance.Layout is not { } layout || await store.FindAsync(layout, cancellationToken) is not { } document || !store.CanOpen(document, user))
        {
            return null;
        }

        return renderer.Render(document, new Dictionary<string, JsonNode?>
        {
            [UiExpressions.Data] = instance.Data.DeepClone(),
            [UiExpressions.Instance] = new JsonObject
            {
                ["id"] = instance.Id.ToString(),
                ["key"] = instance.Key,
                ["state"] = instance.State,
                ["stateTitle"] = instance.StateTitle,
                ["workflow"] = instance.Workflow,
                ["status"] = instance.Status.ToString(),
                ["startedBy"] = instance.StartedBy,
            },
        }, user);
    }
}
