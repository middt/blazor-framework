using System.Text.Json.Nodes;
using Middt.Framework.Workflow.Tasks;
using Rebus.Bus;

namespace Middt.Framework.Rebus;

/// <summary>Bridges the generic workflow <c>publish</c> task (<see cref="IWorkflowMessagePublisher"/>) to Rebus (<see cref="IBus"/>).</summary>
public sealed class RebusMessagePublisher(IBus bus) : IWorkflowMessagePublisher
{
    public Task PublishAsync(string messageType, JsonObject payload, CancellationToken cancellationToken) =>
        bus.Publish(new RebusWorkflowMessage(messageType, payload.ToJsonString()));
}
