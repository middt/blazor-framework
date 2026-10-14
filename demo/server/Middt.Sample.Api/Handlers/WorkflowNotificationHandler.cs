using Middt.Sample.Common.Messages;
using Rebus.Handlers;

namespace Middt.Sample.Api.Handlers;

/// <summary>Demo subscriber for messages published by workflows.</summary>
public sealed partial class WorkflowNotificationHandler(ILogger<WorkflowNotificationHandler> logger) : IHandleMessages<WorkflowNotification>
{
    public Task Handle(WorkflowNotification message)
    {
        LogNotification(logger, message.Type, message.Payload);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Workflow notification {Type}: {Payload}")]
    private static partial void LogNotification(ILogger logger, string type, string payload);
}
