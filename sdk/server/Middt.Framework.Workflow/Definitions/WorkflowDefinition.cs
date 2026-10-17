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

    /// <summary>
    /// Which instances a user may see and drive. Default (<c>null</c> / <c>"all"</c>): every instance, for everyone allowed by
    /// <see cref="Roles"/>. <c>"owner"</c>: only the instances the user started, unless the user has one of
    /// <see cref="InstanceAccessDefinition.SupervisorRoles"/> (e.g. approvers).
    /// </summary>
    public InstanceAccessDefinition? InstanceAccess { get; set; }

    /// <summary>The transition that creates an instance (no <c>from</c>; usually targets the initial state).</summary>
    public TransitionDefinition Start { get; set; } = new() { Key = "start" };

    public List<StateDefinition> States { get; set; } = [];

    /// <summary>Transitions available in several states (<see cref="TransitionDefinition.AvailableIn"/>, <c>"*"</c> = every non-final state).</summary>
    public List<TransitionDefinition> SharedTransitions { get; set; } = [];

    /// <summary>Fires <see cref="WorkflowTimeout.Target"/> when the instance is still running after <see cref="WorkflowTimeout.After"/>.</summary>
    public WorkflowTimeout? Timeout { get; set; }

    /// <summary>Default error handling for tasks that do not define their own.</summary>
    public ErrorPolicy? OnError { get; set; }

    /// <summary>Optional schema for instance data projection (<c>x-roles</c> field visibility and <c>x-mask</c> field masking).</summary>
    public JsonObject? DataSchema { get; set; }

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

    /// <summary>Wizard step number shown as progress in UIs (states of alternative branches may share a number).</summary>
    public int? Step { get; set; }

    public List<TaskDefinition> OnEntry { get; set; } = [];

    public List<TaskDefinition> OnExit { get; set; } = [];

    public List<TransitionDefinition> Transitions { get; set; } = [];

    /// <summary>Only for <see cref="StateType.SubFlow"/>: the child workflow started on entry.</summary>
    public SubFlowDefinition? SubFlow { get; set; }

    /// <summary>Server driven UI: the component that renders an instance while it is in this state.</summary>
    public ViewDefinition? View { get; set; }

    /// <summary>Default error handling for tasks in this state (overrides <see cref="WorkflowDefinition.OnError"/>).</summary>
    public ErrorPolicy? OnError { get; set; }

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

    /// <summary>When <see cref="Target"/> is <c>$self</c>, re-runs <c>onExit</c>, <c>onEntry</c> and resets state timers instead of updating data in place.</summary>
    public bool Reenter { get; set; }

    public TriggerType Trigger { get; set; } = TriggerType.Manual;

    public string? Title { get; set; }

    /// <summary>
    /// Boolean expression. Automatic transitions: taken when true (e.g. <c>data.amount &gt; 5000</c>).
    /// Manual transitions: availability condition – the transition is only offered/executable when true.
    /// </summary>
    public string? Rule { get; set; }

    /// <summary>Marks the automatic "else" branch: evaluated last and always taken.</summary>
    public bool Default { get; set; }

    /// <summary>Delay of a scheduled transition (ISO-8601, TimeSpan, or <c>{{ expression }}</c>).</summary>
    public string? After { get; set; }

    /// <summary>Target UTC timestamp of a scheduled transition (ISO-8601 or <c>{{ expression }}</c>).</summary>
    public string? At { get; set; }

    /// <summary>Roles or dynamic grants (<c>$starter</c>, <c>$previousUser</c>, <c>$user:{{...}}</c>, <c>$role:{{...}}</c>) allowed to execute a manual transition. Empty = any user allowed to read the instance.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Roles or dynamic grants (<c>$starter</c>, <c>$previousUser</c>, <c>$user:{{...}}</c>, <c>$role:{{...}}</c>) forbidden from executing a manual transition (evaluated before <see cref="Roles"/>).</summary>
    public List<string> DenyRoles { get; set; } = [];

    /// <summary>States where a shared transition is offered (<c>"*"</c> = all non-final states).</summary>
    public List<string> AvailableIn { get; set; } = [];

    /// <summary>Request body contract. Without it the body is ignored (no implicit writes into instance data).</summary>
    public InputDefinition? Input { get; set; }

    public List<TaskDefinition> OnExecute { get; set; } = [];

    /// <summary>Server driven UI: a custom input form component replacing the schema generated form.</summary>
    public ViewDefinition? View { get; set; }
}

/// <summary>
/// The UI of a state or transition: a Razor component registered with <c>[WorkflowView("name")]</c>, or (states only) the key of a
/// JSON layout stored by the UI module, which can be added and changed at runtime.
/// </summary>
public sealed class ViewDefinition
{
    /// <summary>Registered view name (optional for transitions with <see cref="Inline"/>: the schema generated form is used).</summary>
    public string? Component { get; set; }

    /// <summary>States only: key of a JSON layout (rendered with <c>data</c> = instance data and <c>instance</c>).</summary>
    public string? Layout { get; set; }

    /// <summary>Transitions only: show the input form inside the instance screen (wizard step) instead of a dialog.</summary>
    public bool Inline { get; set; }
}

/// <summary><c>"instanceAccess": { "mode": "owner", "supervisorRoles": ["manager"] }</c></summary>
public sealed class InstanceAccessDefinition
{
    public const string All = "all";
    public const string Owner = "owner";

    /// <summary><c>all</c> (default) or <c>owner</c>.</summary>
    public string Mode { get; set; } = All;

    /// <summary>Roles that see and drive every instance in <c>owner</c> mode.</summary>
    public List<string> SupervisorRoles { get; set; } = [];

    public bool IsOwnerOnly => string.Equals(Mode, Owner, StringComparison.Ordinal);
}

/// <summary>
/// Validates the request body (<see cref="Schema"/>) and copies it into the instance data. Without a schema the body is
/// ignored and only <see cref="Set"/> expressions are applied.
/// </summary>
public sealed class InputDefinition
{
    /// <summary>JSON Schema (subset: type, properties, required, additionalProperties, enum, min/max, minLength/maxLength, pattern, items; UI hint x-lookup, see <see cref="Schema.JsonSchemaValidator"/>).</summary>
    public JsonObject? Schema { get; set; }

    /// <summary>Data path the validated body is merged into (empty = root).</summary>
    public string? Into { get; set; }

    /// <summary>Extra values computed with expressions after the merge, e.g. <c>{ "approvedBy": "user.name" }</c>.</summary>
    public Dictionary<string, string> Set { get; set; } = [];

    /// <summary>
    /// Top-level input properties that must never be persisted (passwords, one-time codes): tasks and expressions of the
    /// transition see them (<c>input.password</c>), but they are not merged into instance data and are removed from
    /// history, pending (retry) input and job payloads. Such transitions cannot run asynchronously, and a faulted run
    /// must be re-submitted by the user instead of retried.
    /// </summary>
    public List<string> Sensitive { get; set; } = [];

    /// <summary>The input without <see cref="Sensitive"/> properties (the object itself when nothing is sensitive).</summary>
    public JsonObject? Persistable(JsonObject? input)
    {
        if (input is null || Sensitive.Count == 0 || !Sensitive.Any(input.ContainsKey))
        {
            return input;
        }

        var copy = (JsonObject)input.DeepClone();
        foreach (var name in Sensitive)
        {
            copy.Remove(name);
        }

        return copy;
    }
}

public sealed class TaskDefinition
{
    /// <summary>Task type: a built-in (<c>http</c>, <c>set</c>, <c>log</c>, <c>publish</c>, <c>startWorkflow</c>, <c>setKey</c>) or an application task.</summary>
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

    /// <summary>Delay before the first retry; multiplied by <see cref="BackoffMultiplier"/> on each further retry.</summary>
    public string Delay { get; set; } = "00:00:01";

    /// <summary>Multiplier applied to <see cref="Delay"/> after each retry attempt (default 2.0).</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Optional cap on retry delay (ISO-8601 or TimeSpan).</summary>
    public string? MaxDelay { get; set; }

    /// <summary>Adds ±20% randomization to retry delays to prevent thundering herds.</summary>
    public bool UseJitter { get; set; }

    /// <summary>HTTP status codes (e.g. <c>"502"</c>, <c>"503"</c>, <c>"5xx"</c>, <c>"!400"</c>, <c>"!4xx"</c>) eligible for retry. Empty = retry all errors.</summary>
    public List<string> RetryOnStatusCodes { get; set; } = [];

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

    /// <summary>Forwards non-cancellation manual transitions executed on the parent directly to the active child subflow (default <c>true</c>).</summary>
    public bool ForwardTransitions { get; set; } = true;
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
