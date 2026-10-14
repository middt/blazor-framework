using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Middt.Framework.Model.Workflow;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowInstanceStatus>))]
public enum WorkflowInstanceStatus
{
    /// <summary>Waiting for the next transition (or for a subflow).</summary>
    Active,

    /// <summary>A transition is executing; further transitions are rejected with 409.</summary>
    Busy,

    Completed,

    /// <summary>A task failed; see incidents and use retry.</summary>
    Faulted,

    Cancelled,
}

public sealed class StartWorkflowRequest
{
    /// <summary>Optional business key (unique per workflow); starting twice with the same key returns the existing instance.</summary>
    public string? Key { get; set; }

    /// <summary>Validated against the start transition's input schema.</summary>
    public JsonObject? Data { get; set; }
}

public sealed class WorkflowSummaryModel
{
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? Title { get; set; }
}

public sealed class WorkflowStateModel
{
    public string Key { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SubType { get; set; } = string.Empty;

    /// <summary>Name of the registered view component for this state (server driven UI), if any.</summary>
    public string? View { get; set; }

    /// <summary>Key of the JSON layout (server driven UI) shown for this state, if any.</summary>
    public string? Layout { get; set; }

    /// <summary>Wizard step number (progress indicator), if the state is a step.</summary>
    public int? Step { get; set; }
}

public sealed class WorkflowTransitionModel
{
    public string Key { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? From { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
}

/// <summary>States and transitions of a workflow (e.g. for diagrams).</summary>
public sealed class WorkflowDefinitionModel
{
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? Title { get; set; }
    public string StartState { get; set; } = string.Empty;
    public JsonObject? StartInputSchema { get; set; }
    public List<WorkflowStateModel> States { get; set; } = [];
    public List<WorkflowTransitionModel> Transitions { get; set; } = [];
}

/// <summary>A manual transition the caller may execute in the instance's current state.</summary>
public sealed class AvailableTransitionModel
{
    public string Key { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Target { get; set; } = string.Empty;

    /// <summary>JSON Schema of the expected request body, when the transition takes input.</summary>
    public JsonObject? InputSchema { get; set; }

    /// <summary>Name of a registered input form component that replaces the schema generated form, if any.</summary>
    public string? View { get; set; }

    /// <summary>Render the input form inside the instance screen (wizard step) instead of a dialog.</summary>
    public bool Inline { get; set; }

    /// <summary>Data path the input is stored under (lets UIs pre-fill the form when a step is revisited).</summary>
    public string? InputInto { get; set; }
}

public sealed class ScheduledTransitionModel
{
    public string Key { get; set; } = string.Empty;
    public DateTime DueAt { get; set; }
}

public sealed class WorkflowInstanceModel
{
    public Guid Id { get; set; }
    public string? Key { get; set; }
    public string Workflow { get; set; } = string.Empty;
    public int WorkflowVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public string? StateTitle { get; set; }
    public string StateType { get; set; } = string.Empty;
    public string StateSubType { get; set; } = string.Empty;

    /// <summary>Name of the view component of the current state (server driven UI), if any.</summary>
    public string? View { get; set; }

    /// <summary>Key of the JSON layout of the current state (rendered with <c>data</c> and <c>instance</c>), if any.</summary>
    public string? Layout { get; set; }

    public WorkflowInstanceStatus Status { get; set; }
    public JsonObject Data { get; set; } = [];
    public int DataVersion { get; set; }
    public Guid? ParentInstanceId { get; set; }
    public Guid? WaitingForInstanceId { get; set; }
    public string? Error { get; set; }
    public string? StartedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<AvailableTransitionModel> AvailableTransitions { get; set; } = [];
    public List<ScheduledTransitionModel> ScheduledTransitions { get; set; } = [];
}

public sealed class WorkflowHistoryModel
{
    public string TransitionKey { get; set; } = string.Empty;
    public string? FromState { get; set; }
    public string ToState { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string? ExecutedBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int DataVersion { get; set; }
}

public sealed class WorkflowIncidentModel
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? TransitionKey { get; set; }
    public string? TaskKey { get; set; }
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
