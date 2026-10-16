using System.Text;
using Middt.Framework.Workflow.Tasks;
using Rebus.Bus;

namespace Middt.Framework.Plugin.Rebus;

/// <summary>
/// Bridges the workflow <see cref="IWorkflowRabbitMqPublisher"/> to Rebus (<see cref="IBus"/>),
/// routing publish/send calls through Rebus's RabbitMQ or In-Memory transport.
/// </summary>
public sealed class RebusRabbitMqPublisher(IBus bus) : IWorkflowRabbitMqPublisher
{
    public async Task PublishAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IDictionary<string, object?>? headers = null,
        string? contentType = "application/json",
        bool persistent = true,
        CancellationToken cancellationToken = default)
    {
        var rebusHeaders = new Dictionary<string, string>();
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
            {
                if (v is not null)
                {
                    rebusHeaders[k] = v.ToString() ?? string.Empty;
                }
            }
        }

        if (!string.IsNullOrEmpty(contentType))
        {
            rebusHeaders["Content-Type"] = contentType;
        }

        var payloadString = Encoding.UTF8.GetString(body.Span);
        var topic = !string.IsNullOrWhiteSpace(routingKey)
            ? routingKey
            : (!string.IsNullOrWhiteSpace(exchange) ? exchange : "default");

        // If exchange is empty and routingKey is set, route directly to that queue
        if (string.IsNullOrWhiteSpace(exchange) && !string.IsNullOrWhiteSpace(routingKey))
        {
            await bus.Advanced.Routing.Send(routingKey, new RebusWorkflowMessage(topic, payloadString), rebusHeaders);
        }
        else
        {
            await bus.Advanced.Topics.Publish(topic, new RebusWorkflowMessage(topic, payloadString), rebusHeaders);
        }
    }
}
