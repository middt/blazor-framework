using Middt.Framework.Model.Workflow;

namespace Middt.Framework.Workflow.Persistence;

public sealed class WorkflowInstance
{
    public Guid Id { get; set; }

    public string WorkflowKey { get; set; } = string.Empty;

    public int WorkflowVersion { get; set; }

    /// <summary>Optional business key, unique per workflow.</summary>
    public string? BusinessKey { get; set; }

    /// <summary>Current state; empty until the start transition completed.</summary>
    public string State { get; set; } = string.Empty;

    public WorkflowInstanceStatus Status { get; set; }

    /// <summary>Latest instance data (JSON).</summary>
    public string Data { get; set; } = "{}";

    public int DataVersion { get; set; }

    /// <summary>Optimistic concurrency token, incremented on every change.</summary>
    public int Revision { get; set; }

    public DateTime? BusySince { get; set; }

    public Guid? ParentInstanceId { get; set; }

    /// <summary>Child instance this instance waits for (subflow state).</summary>
    public Guid? WaitingForInstanceId { get; set; }

    /// <summary>Transition to re-run by <c>retry</c> when the instance is Faulted.</summary>
    public string? PendingTransition { get; set; }

    /// <summary>Request body of <see cref="PendingTransition"/>.</summary>
    public string? PendingInput { get; set; }

    public string? Error { get; set; }

    public string? StartedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsTerminal => Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled;
}

/// <summary>Audit trail: one row per executed transition with the resulting data snapshot.</summary>
public sealed class WorkflowHistory
{
    public long Id { get; set; }
    public Guid InstanceId { get; set; }
    public string TransitionKey { get; set; } = string.Empty;
    public string? FromState { get; set; }
    public string ToState { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string? ExecutedBy { get; set; }
    public string? Input { get; set; }
    public int DataVersion { get; set; }
    public string Data { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}

public enum WorkflowJobKind
{
    /// <summary>Scheduled transition armed on entering a state; dropped when the state is left.</summary>
    Scheduled,

    /// <summary>Workflow level timeout.</summary>
    Timeout,

    /// <summary>Transition requested asynchronously (HTTP 202).</summary>
    Transition,

    /// <summary>Resume a parent whose subflow finished while the parent was busy.</summary>
    ResumeParent,

    /// <summary>Cancel a child after its parent commits a cancellation transition.</summary>
    CancelChild,

    /// <summary>Deliver an event committed with the workflow state.</summary>
    Event,
}

/// <summary>Durable timer / deferred work item processed by <c>WorkflowJobProcessor</c>.</summary>
public sealed class WorkflowJob
{
    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public WorkflowJobKind Kind { get; set; }
    public string? TransitionKey { get; set; }

    /// <summary>State the job was armed in (scheduled transitions only fire while the instance is still there).</summary>
    public string? State { get; set; }

    public string? Payload { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class WorkflowIncident
{
    public long Id { get; set; }
    public Guid InstanceId { get; set; }
    public string? State { get; set; }
    public string? TransitionKey { get; set; }
    public string? TaskKey { get; set; }
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
