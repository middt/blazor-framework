using System.Security.Cryptography;
using System.Text;
using Middt.Framework.Model.Workflow;

namespace Middt.Framework.Workflow.Persistence;

public sealed class WorkflowInstance
{
    private Guid id;

    public Guid Id
    {
        get => id;
        set
        {
            id = value;
            if (InstanceData is not null)
            {
                InstanceData.InstanceId = value;
            }
        }
    }

    public string WorkflowKey { get; set; } = string.Empty;

    public int WorkflowVersion { get; set; }

    /// <summary>Optional business key, unique per workflow.</summary>
    public string? BusinessKey { get; set; }

    /// <summary>Current state; empty until the start transition completed.</summary>
    public string State { get; set; } = string.Empty;

    public WorkflowInstanceStatus Status { get; set; }

    /// <summary>Navigation to the instance's JSON data row in the <c>WorkflowInstanceData</c> table.</summary>
    public WorkflowInstanceData? InstanceData { get; set; }

    /// <summary>Latest instance data (JSON), stored in <see cref="WorkflowInstanceData"/> (not a column on <c>WorkflowInstances</c>).</summary>
    public string Data
    {
        get => InstanceData?.Data ?? "{}";
        set
        {
            var now = UpdatedAt != default ? UpdatedAt : DateTime.UtcNow;
            InstanceData ??= new WorkflowInstanceData
            {
                InstanceId = id,
                WorkflowKey = WorkflowKey,
                DataVersion = 1,
                UpdatedAt = now,
            };
            InstanceData.WorkflowKey = WorkflowKey;
            InstanceData.Data = value;
            InstanceData.DataHash = WorkflowInstanceData.ComputeHash(value);
            InstanceData.ETag = WorkflowInstanceData.NewETag(id, InstanceData.DataVersion);
            InstanceData.UpdatedAt = now;
        }
    }

    /// <summary>Data version counter, stored in <see cref="WorkflowInstanceData"/> (not a column on <c>WorkflowInstances</c>).</summary>
    public int DataVersion
    {
        get => InstanceData?.DataVersion ?? 0;
        set
        {
            var now = UpdatedAt != default ? UpdatedAt : DateTime.UtcNow;
            InstanceData ??= new WorkflowInstanceData
            {
                InstanceId = id,
                WorkflowKey = WorkflowKey,
                UpdatedAt = now,
            };
            InstanceData.WorkflowKey = WorkflowKey;
            InstanceData.DataVersion = value;
            InstanceData.ETag = WorkflowInstanceData.NewETag(id, value);
            InstanceData.UpdatedAt = now;
        }
    }

    /// <summary>Optimistic concurrency token, incremented on every change.</summary>
    public int Revision { get; set; }

    public DateTime? BusySince { get; set; }

    public Guid? ParentInstanceId { get; set; }

    /// <summary>Child instance this instance waits for (subflow state).</summary>
    public Guid? WaitingForInstanceId { get; set; }

    /// <summary>Background task job this instance waits for (<c>"background": true</c> onEntry task); cleared when it finishes or the state is left.</summary>
    public Guid? BackgroundTaskId { get; set; }

    /// <summary><c>history: delta</c>: patch rows written since the last snapshot row; <c>null</c> until the first history row.</summary>
    public int? HistoryPatchCount { get; set; }

    /// <summary>Transition to re-run by <c>retry</c> when the instance is Faulted.</summary>
    public string? PendingTransition { get; set; }

    /// <summary>Request body of <see cref="PendingTransition"/>.</summary>
    public string? PendingInput { get; set; }

    public string? Error { get; set; }

    public string? StartedBy { get; set; }

    /// <summary>Actor who executed the last manual transition (used by <c>$previousUser</c> role rules).</summary>
    public string? LastExecutedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsTerminal => Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled;
}

/// <summary>
/// Latest JSON data payload and version metadata of a <see cref="WorkflowInstance"/>, stored in the dedicated
/// <c>WorkflowInstanceData</c> table so lightweight state/status operations on <c>WorkflowInstances</c> stay separated from JSON payloads.
/// </summary>
public sealed class WorkflowInstanceData
{
    /// <summary>SHA-256 hex digest of <c>{}</c>.</summary>
    public const string EmptyJsonObjectHash = "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a";

    public Guid InstanceId { get; set; }

    /// <summary>Workflow definition key for partition routing and index isolation.</summary>
    public string WorkflowKey { get; set; } = string.Empty;

    /// <summary>Latest instance data (JSON).</summary>
    public string Data { get; set; } = "{}";

    public int DataVersion { get; set; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Data"/> for fast change detection.</summary>
    public string DataHash { get; set; } = EmptyJsonObjectHash;

    /// <summary>Entity tag for the data payload version.</summary>
    public string ETag { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    public static string ComputeHash(string? json)
    {
        var normalized = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string NewETag(Guid instanceId, int dataVersion) =>
        $"{instanceId:N}-v{dataVersion}";
}

/// <summary>Audit trail: one row per executed transition with the resulting data snapshot.</summary>
public sealed class WorkflowHistory
{
    public long Id { get; set; }
    public Guid InstanceId { get; set; }
    public string WorkflowKey { get; set; } = string.Empty;
    public string TransitionKey { get; set; } = string.Empty;
    public string? FromState { get; set; }
    public string ToState { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string? ExecutedBy { get; set; }
    public string? Input { get; set; }
    public int DataVersion { get; set; }

    /// <summary>Full data snapshot after the transition (<c>history: full</c>); <c>null</c> in <c>delta</c> mode.</summary>
    public string? Data { get; set; }

    /// <summary>RFC 6902 JSON Patch from the previous version to this one (<c>history: delta</c>); <c>null</c> in <c>full</c> mode.</summary>
    public string? Patch { get; set; }

    /// <summary>JSON array of <see cref="WorkflowTaskExecutionModel"/> executed during this transition.</summary>
    public string? Tasks { get; set; }

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

    /// <summary>Run a <c>"background": true</c> onEntry task outside the instance lock and merge its result.</summary>
    Task,

    /// <summary>A message for an event transition, buffered until the instance can take it (or it expires).</summary>
    Message,
}

/// <summary>Durable timer / deferred work item processed by <c>WorkflowJobProcessor</c>.</summary>
public sealed class WorkflowJob
{
    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public string WorkflowKey { get; set; } = string.Empty;
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
    public string WorkflowKey { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? TransitionKey { get; set; }
    public string? TaskKey { get; set; }
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
