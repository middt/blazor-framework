using System.Text.Json.Nodes;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Common.Messages;
using Rebus.Bus;

namespace Middt.Sample.Api;

/// <summary>Bridges the workflow <c>publish</c> task to Rebus.</summary>
public sealed class RebusWorkflowMessagePublisher(IBus bus) : IWorkflowMessagePublisher
{
    public Task PublishAsync(string messageType, JsonObject payload, CancellationToken cancellationToken) =>
        bus.Publish(new WorkflowNotification(messageType, payload.ToJsonString()));
}
