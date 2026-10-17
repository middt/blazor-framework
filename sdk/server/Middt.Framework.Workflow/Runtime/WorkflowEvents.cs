using System.Text.Json.Nodes;

namespace Middt.Framework.Workflow.Runtime;

public enum WorkflowEventType
{
    Started,
    StateChanged,
    Completed,
    Cancelled,
    Faulted,
}

/// <summary>Queued with the state change and delivered after commit; handlers may receive it again after failure or restart.</summary>
public sealed record WorkflowEvent(
    WorkflowEventType Type,
    Guid InstanceId,
    string Workflow,
    int WorkflowVersion,
    string? BusinessKey,
    string? FromState,
    string State,
    string? Transition,
    JsonObject Data,
    DateTime OccurredAt);

/// <summary>Observe workflow changes (notifications, integration events, projections). Register any number of handlers.</summary>
public interface IWorkflowEventHandler
{
    Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken);
}

/// <summary>Wakes the job processor when new work is due immediately.</summary>
public sealed class WorkflowJobSignal
{
    private readonly System.Threading.Channels.Channel<bool> channel =
        System.Threading.Channels.Channel.CreateBounded<bool>(new System.Threading.Channels.BoundedChannelOptions(1)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
        });

    public void Notify() => channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // polling interval elapsed
        }
    }
}
