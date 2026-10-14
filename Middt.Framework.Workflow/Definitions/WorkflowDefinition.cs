using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Middt.Framework.Workflow.Definitions;

/// <summary>
/// A workflow (state machine) loaded from JSON. Identified by <see cref="Key"/> + <see cref="Version"/>; new instances use the
/// highest version, running instances stay on the version they were started with.
/// </summary>
public sealed class WorkflowDefinition
{
    public string Key { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public string? Title { get; set; }

    /// <summary>Roles allowed to start, list and read instances. Empty = any authenticated user.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>The transition that creates an instance (no <c>from</c>; usually targets the initial state).</summary>
    public TransitionDefinition Start { get; set; } = new() { Key = "start" };

    public List<StateDefinition> States { get; set; } = [];

    /// <summary>Transitions available in several states (<see cref="TransitionDefinition.AvailableIn"/>, <c>"*"</c> = every non-final state).</summary>
    public List<TransitionDefinition> SharedTransitions { get; set; } = [];

    /// <summary>Fires <see cref="WorkflowTimeout.Target"/> when the instance is still running after <see cref="WorkflowTimeout.After"/>.</summary>
    public WorkflowTimeout? Timeout { get; set; }

    /// <summary>Default error handling for tasks that do not define their own.</summary>
    public ErrorPolicy? OnError { get; set; }

    public StateDefinition? FindState(string key) => States.FirstOrDefault(s => s.Key == key);
}

public sealed class WorkflowTimeout
{
    /// <summary>Duration as ISO-8601 (<c>PT48H</c>) or .NET TimeSpan (<c>2.00:00:00</c>).</summary>
    public string After { get; set; } = string.Empty;

    /// <summary>State the instance moves to on timeout.</summary>
    public string Target { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter<StateType>))]
public enum StateType
{
    Initial,
    Intermediate,
    Finish,
    SubFlow,
}

/// <summary>Business meaning of a state, used by UIs and reports (and by parents to inspect a finished subflow).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StateSubType>))]
public enum StateSubType
{
    None,
    Human,
    Success,
    Error,
    Cancelled,
    Timeout,
}

public sealed class StateDefinition
{
    public string Key { get; set; } = string.Empty;

    public StateType Type { get; set; } = StateType.Intermediate;

    public StateSubType SubType { get; set; }

    public string? Title { get; set; }

    public List<TaskDefinition> OnEntry { get; set; } = [];

    public List<TaskDefinition> OnExit { get; set; } = [];

    public List<TransitionDefinition> Transitions { get; set; } = [];

    /// <summary>Only for <see cref="StateType.SubFlow"/>: the child workflow started on entry.</summary>
    public SubFlowDefinition? SubFlow { get; set; }

    [JsonIgnore]
    public bool IsFinal => Type == StateType.Finish;
}

[JsonConverter(typeof(JsonStringEnumConverter<TriggerType>))]
public enum TriggerType
{
    /// <summary>Executed by a user / API call.</summary>
    Manual,

    /// <summary>Evaluated after entering the state; the first transition whose <c>rule</c> is true is taken.</summary>
    Automatic,

    /// <summary>Executed by the scheduler <c>after</c> the given delay since entering the state.</summary>
    Scheduled,
}

public sealed class TransitionDefinition
{
    public const string Self = "$self";

    public string Key { get; set; } = string.Empty;

    /// <summary>Target state key, or <c>$self</c> to stay in the current state (data update).</summary>
    public string Target { get; set; } = string.Empty;

    public TriggerType Trigger { get; set; } = TriggerType.Manual;

    public string? Title { get; set; }

    /// <summary>Boolean expression for automatic transitions, e.g. <c>data.amount &gt; 5000</c>.</summary>
    public string? Rule { get; set; }

    /// <summary>Marks the automatic "else" branch: evaluated last and always taken.</summary>
    public bool Default { get; set; }

    /// <summary>Delay of a scheduled transition (ISO-8601 or TimeSpan).</summary>
    public string? After { get; set; }

    /// <summary>Roles allowed to execute a manual transition. Empty = any user allowed to read the instance.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>States where a shared transition is offered (<c>"*"</c> = all non-final states).</summary>
    public List<string> AvailableIn { get; set; } = [];

    /// <summary>Request body contract. Without it the body is ignored (no implicit writes into instance data).</summary>
    public InputDefinition? Input { get; set; }

    public List<TaskDefinition> OnExecute { get; set; } = [];
}

/// <summary>Validates the request body and copies it into the instance data.</summary>
public sealed class InputDefinition
{
    /// <summary>JSON Schema (subset: type, properties, required, additionalProperties, enum, min/max, minLength/maxLength, pattern, items).</summary>
    public JsonObject? Schema { get; set; }

    /// <summary>Data path the validated body is merged into (empty = root).</summary>
    public string? Into { get; set; }

    /// <summary>Extra values computed with expressions after the merge, e.g. <c>{ "approvedBy": "user.name" }</c>.</summary>
    public Dictionary<string, string> Set { get; set; } = [];
}

public sealed class TaskDefinition
{
    /// <summary>Task type: a built-in (<c>http</c>, <c>set</c>, <c>log</c>, <c>publish</c>, <c>startWorkflow</c>) or an application task.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>Identifies the task in history/incidents (defaults to the task type).</summary>
    public string? Key { get; set; }

    /// <summary>Optional boolean expression; the task is skipped when false.</summary>
    public string? When { get; set; }

    public JsonObject Config { get; set; } = [];

    public ErrorPolicy? OnError { get; set; }

    [JsonIgnore]
    public string DisplayKey => string.IsNullOrEmpty(Key) ? Task : Key;
}

[JsonConverter(typeof(JsonStringEnumConverter<ErrorAction>))]
public enum ErrorAction
{
    /// <summary>Mark the instance Faulted (an incident is recorded; retry via the API).</summary>
    Fault,

    /// <summary>Execute <see cref="ErrorPolicy.Transition"/> instead of continuing.</summary>
    Transition,

    /// <summary>Record an incident and continue with the next task.</summary>
    Continue,
}

public sealed class ErrorPolicy
{
    /// <summary>Total attempts (1 = no retry).</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Delay before the first retry; doubles on each further retry.</summary>
    public string Delay { get; set; } = "00:00:01";

    public ErrorAction Action { get; set; } = ErrorAction.Fault;

    public string? Transition { get; set; }
}

public sealed class SubFlowDefinition
{
    public string Workflow { get; set; } = string.Empty;

    /// <summary>Child data built from the parent: target path → expression (empty = copy the whole parent data).</summary>
    public Dictionary<string, string> Input { get; set; } = [];

    /// <summary>Parent data path the child's final data is stored under (empty = merge into root).</summary>
    public string? OutputInto { get; set; }
}

public static class WorkflowJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
