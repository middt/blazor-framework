using System.Text.Json.Nodes;

namespace Middt.Framework.Plugin.Rebus;

/// <summary>Standard JSON message envelope for workflow messages dispatched through Rebus.</summary>
public sealed record RebusWorkflowMessage(string Type, string Payload)
{
    /// <summary>Parses the payload string into a <see cref="JsonNode"/> if valid JSON; otherwise null.</summary>
    public JsonNode? AsJson() => string.IsNullOrWhiteSpace(Payload) ? null : JsonNode.Parse(Payload);
}
