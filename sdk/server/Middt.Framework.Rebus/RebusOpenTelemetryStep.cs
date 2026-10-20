using System.Diagnostics;
using Middt.Framework.Common.Observability;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace Middt.Framework.Rebus;

/// <summary>
/// Rebus outgoing/incoming pipeline step that propagates W3C trace context (<c>traceparent</c> / <c>tracestate</c>)
/// through Rebus message headers and emits <c>messaging.rebus.publish</c> (<see cref="ActivityKind.Producer"/>) and
/// <c>messaging.rebus.process</c> (<see cref="ActivityKind.Consumer"/>) OpenTelemetry spans.
/// </summary>
[StepDocumentation("Propagates W3C traceparent headers and emits OpenTelemetry Producer/Consumer spans for Rebus messages.")]
public sealed class RebusOpenTelemetryStep : IOutgoingStep, IIncomingStep
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";

    /// <inheritdoc />
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var message = context.Load<Message>();
        var destinations = context.Load<DestinationAddresses>();
        var destination = destinations is not null ? string.Join(",", destinations) : "unknown";
        var messageType = GetShortMessageType(message?.Headers);

        using var activity = MiddtDiagnostics.ApiActivitySource.StartActivity(
            $"messaging.rebus.publish {messageType}",
            ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "rebus");
            activity.SetTag("messaging.operation", "publish");
            activity.SetTag("messaging.destination.name", destination);
            activity.SetTag("messaging.message.type", messageType);
            if (message?.Headers is not null && message.Headers.TryGetValue(Headers.MessageId, out var messageId))
            {
                activity.SetTag("messaging.message.id", messageId);
            }

            if (message?.Headers is not null && !string.IsNullOrEmpty(activity.Id))
            {
                message.Headers[TraceParentHeader] = activity.Id;
                if (!string.IsNullOrEmpty(activity.TraceStateString))
                {
                    message.Headers[TraceStateHeader] = activity.TraceStateString;
                }
            }
        }

        try
        {
            await next();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var transportMessage = context.Load<TransportMessage>();
        var headers = transportMessage?.Headers;
        var messageType = GetShortMessageType(headers);

        ActivityContext parentContext = default;
        if (headers is not null &&
            headers.TryGetValue(TraceParentHeader, out var traceParent) &&
            !string.IsNullOrWhiteSpace(traceParent))
        {
            headers.TryGetValue(TraceStateHeader, out var traceState);
            ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parentContext);
        }

        using var activity = parentContext != default
            ? MiddtDiagnostics.ApiActivitySource.StartActivity(
                $"messaging.rebus.process {messageType}",
                ActivityKind.Consumer,
                parentContext)
            : MiddtDiagnostics.ApiActivitySource.StartActivity(
                $"messaging.rebus.process {messageType}",
                ActivityKind.Consumer);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "rebus");
            activity.SetTag("messaging.operation", "process");
            activity.SetTag("messaging.message.type", messageType);
            if (headers is not null && headers.TryGetValue(Headers.MessageId, out var messageId))
            {
                activity.SetTag("messaging.message.id", messageId);
            }
        }

        try
        {
            await next();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static string GetShortMessageType(Dictionary<string, string>? headers)
    {
        if (headers is null || !headers.TryGetValue(Headers.Type, out var rawType) || string.IsNullOrWhiteSpace(rawType))
        {
            return "message";
        }

        var commaIndex = rawType.IndexOf(',', StringComparison.Ordinal);
        var fullTypeName = commaIndex > 0 ? rawType[..commaIndex].Trim() : rawType.Trim();
        var dotIndex = fullTypeName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex + 1 < fullTypeName.Length
            ? fullTypeName[(dotIndex + 1)..]
            : fullTypeName;
    }
}
