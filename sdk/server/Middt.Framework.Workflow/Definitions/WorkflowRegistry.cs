using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Schema;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Workflow.Definitions;

/// <summary>Loads, validates and indexes the workflow definitions (all versions).</summary>
public sealed class WorkflowRegistry
{
    private readonly IServiceProvider services;
    private readonly IOptions<MiddtWorkflowOptions> options;
    private readonly WorkflowExpressionEngine expressions;
    private readonly WorkflowTaskRegistry tasks;
    private readonly Lock sync = new();
    private volatile IReadOnlyDictionary<string, SortedList<int, WorkflowDefinition>>? currentDefinitions;

    public WorkflowRegistry(IServiceProvider services, IOptions<MiddtWorkflowOptions> options, WorkflowExpressionEngine expressions, WorkflowTaskRegistry tasks)
    {
        this.services = services;
        this.options = options;
        this.expressions = expressions;
        this.tasks = tasks;
    }

    private IReadOnlyDictionary<string, SortedList<int, WorkflowDefinition>> Definitions
    {
        get
        {
            if (currentDefinitions is not null)
            {
                return currentDefinitions;
            }

            lock (sync)
            {
                return currentDefinitions ??= Load(services, options.Value, expressions, tasks);
            }
        }
    }

    /// <summary>Loads and validates all definitions; throws <see cref="WorkflowDefinitionException"/> listing every problem.</summary>
    public void EnsureLoaded() => _ = Definitions;

    /// <summary>Reloads definitions from all configured sources at runtime without restarting.</summary>
    public void Reload()
    {
        lock (sync)
        {
            currentDefinitions = Load(services, options.Value, expressions, tasks);
        }
    }

    public IEnumerable<WorkflowDefinition> Latest => Definitions.Values.Select(v => v.Values[^1]);

    /// <summary>All registered versions of one workflow, for version-specific instance access checks.</summary>
    public IReadOnlyList<WorkflowDefinition> GetVersions(string key) =>
        Definitions.TryGetValue(key, out var versions)
            ? versions.Values.ToArray()
            : throw new WorkflowNotFoundException($"Workflow '{key}' does not exist.");

    public WorkflowDefinition GetLatest(string key) =>
        Definitions.TryGetValue(key, out var versions)
            ? versions.Values[^1]
            : throw new WorkflowNotFoundException($"Workflow '{key}' does not exist.");

    public WorkflowDefinition Get(string key, int version) =>
        Definitions.TryGetValue(key, out var versions) && versions.TryGetValue(version, out var definition)
            ? definition
            : throw new WorkflowNotFoundException($"Workflow '{key}' version {version} does not exist.");

    private static Dictionary<string, SortedList<int, WorkflowDefinition>> Load(
        IServiceProvider services,
        MiddtWorkflowOptions options,
        WorkflowExpressionEngine expressions,
        WorkflowTaskRegistry tasks)
    {
        var errors = new List<string>();
        var result = new Dictionary<string, SortedList<int, WorkflowDefinition>>(StringComparer.Ordinal);

        foreach (var (source, json) in options.DefinitionSources.SelectMany(s => s(services)))
        {
            WorkflowDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<WorkflowDefinition>(json, WorkflowJson.Options);
            }
            catch (JsonException ex)
            {
                errors.Add($"{source}: {ex.Message}");
                continue;
            }

            if (definition is null || string.IsNullOrWhiteSpace(definition.Key))
            {
                errors.Add($"{source}: 'key' is required.");
                continue;
            }

            if (!result.TryGetValue(definition.Key, out var versions))
            {
                versions = [];
                result[definition.Key] = versions;
            }

            if (!versions.TryAdd(definition.Version, definition))
            {
                errors.Add($"{source}: workflow '{definition.Key}' version {definition.Version} is defined twice.");
            }
        }

        // Tasks are resolved for their Validate method: use a scope, application tasks usually depend on scoped services.
        using var scope = services.CreateScope();
        foreach (var definition in result.Values.SelectMany(v => v.Values))
        {
            errors.AddRange(WorkflowDefinitionValidator.Validate(definition, expressions, tasks, scope.ServiceProvider, key => result.ContainsKey(key), options.ViewTypes)
                .Select(e => $"{definition.Key} v{definition.Version}: {e}"));
        }

        return errors.Count > 0 ? throw new WorkflowDefinitionException(errors) : result;
    }
}

/// <summary>Name → task type map (built-ins plus application tasks).</summary>
public sealed class WorkflowTaskRegistry(IOptions<MiddtWorkflowOptions> options)
{
    private static readonly Dictionary<string, Type> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["set"] = typeof(SetTask),
        ["log"] = typeof(LogTask),
        ["http"] = typeof(HttpTask),
        ["http-request"] = typeof(HttpTask),
        ["httpRequest"] = typeof(HttpTask),
        ["rabbitmq"] = typeof(RabbitMqTask),
        ["rabbit"] = typeof(RabbitMqTask),
        ["rabbit-publish"] = typeof(RabbitMqTask),
        ["rabbitPublish"] = typeof(RabbitMqTask),
        ["publish"] = typeof(PublishTask),
        ["operation"] = typeof(OperationTask),
        ["startWorkflow"] = typeof(StartWorkflowTask),
        ["start-workflow"] = typeof(StartWorkflowTask),
        ["setKey"] = typeof(SetKeyTask),
        ["set-key"] = typeof(SetKeyTask),
        // Generic DB / Entity tasks
        ["entity-create"] = typeof(EntityCreateTask),
        ["entity-insert"] = typeof(EntityCreateTask),
        ["entityCreate"] = typeof(EntityCreateTask),
        ["entityInsert"] = typeof(EntityCreateTask),
        ["entity-update"] = typeof(EntityUpdateTask),
        ["entityUpdate"] = typeof(EntityUpdateTask),
        ["entity-load"] = typeof(EntityLoadTask),
        ["entity-find"] = typeof(EntityLoadTask),
        ["entityLoad"] = typeof(EntityLoadTask),
        ["entityFind"] = typeof(EntityLoadTask),
        ["entity-delete"] = typeof(EntityDeleteTask),
        ["entityDelete"] = typeof(EntityDeleteTask),
        ["db-sql"] = typeof(DbSqlTask),
        ["db-execute"] = typeof(DbSqlTask),
        ["dbSql"] = typeof(DbSqlTask),
        ["dbExecute"] = typeof(DbSqlTask),
        ["db-query"] = typeof(DbQueryTask),
        ["dbQuery"] = typeof(DbQueryTask),
        ["fanOut"] = typeof(FanOutTask),
        ["fan-out"] = typeof(FanOutTask),
        ["cache"] = typeof(CacheTask),
    };

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type> runtimeCustomTasks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers or overrides a workflow task type at runtime without redeploying or restarting the application.</summary>
    public void Register(string name, Type taskType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(taskType);
        if (!typeof(IWorkflowTask).IsAssignableFrom(taskType) || taskType.IsAbstract || !taskType.IsClass)
        {
            throw new ArgumentException($"Task type '{taskType.FullName}' must be a non-abstract class implementing {nameof(IWorkflowTask)}.", nameof(taskType));
        }

        runtimeCustomTasks[name] = taskType;
    }

    public Type? Find(string name) =>
        runtimeCustomTasks.TryGetValue(name, out var runtimeType)
            ? runtimeType
            : options.Value.TaskTypes.TryGetValue(name, out var custom)
                ? custom
                : BuiltIns.GetValueOrDefault(name);

    public IWorkflowTask Resolve(IServiceProvider services, string name) =>
        Find(name) is { } type
            ? (IWorkflowTask)ActivatorUtilities.GetServiceOrCreateInstance(services, type)
            : throw new InvalidOperationException($"Unknown workflow task '{name}'.");
}

internal static class WorkflowDefinitionValidator
{
    public static IEnumerable<string> Validate(
        WorkflowDefinition wf,
        WorkflowExpressionEngine expressions,
        WorkflowTaskRegistry tasks,
        IServiceProvider services,
        Func<string, bool> workflowExists,
        IReadOnlyDictionary<string, Type> views)
    {
        foreach (var error in ValidateViews(wf, views))
        {
            yield return error;
        }

        if (wf.DataSchema is { } dataSchema)
        {
            foreach (var error in Safely(() => JsonSchemaValidator.CheckSchema(dataSchema)))
            {
                yield return $"dataSchema {error}";
            }
        }

        if (wf.InstanceAccess is { } access && access.Mode is not (InstanceAccessDefinition.All or InstanceAccessDefinition.Owner))
        {
            yield return $"instanceAccess.mode must be '{InstanceAccessDefinition.All}' or '{InstanceAccessDefinition.Owner}'.";
        }

        if (wf.Version < 1)
        {
            yield return "version must be ≥ 1.";
        }

        if (wf.States.Count == 0)
        {
            yield return "at least one state is required.";
            yield break;
        }

        foreach (var duplicate in wf.States.GroupBy(s => s.Key).Where(g => g.Count() > 1))
        {
            yield return $"state '{duplicate.Key}' is defined more than once.";
        }

        var stateKeys = wf.States.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var allTransitionKeys = wf.States.SelectMany(s => s.Transitions).Concat(wf.SharedTransitions).Select(t => t.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var error in ValidateTransition(wf.Start, "start", stateKeys, allTransitionKeys, expressions, tasks, services))
        {
            yield return error;
        }

        if (wf.Timeout is { } timeout)
        {
            if (!Durations.TryParse(timeout.After, out _))
            {
                yield return "timeout.after is not a valid duration.";
            }

            if (!stateKeys.Contains(timeout.Target))
            {
                yield return $"timeout.target '{timeout.Target}' is not a state.";
            }
        }

        foreach (var error in ValidatePolicy(wf.OnError, "onError", allTransitionKeys))
        {
            yield return error;
        }

        foreach (var state in wf.States)
        {
            var where = $"state '{state.Key}'";

            foreach (var error in ValidatePolicy(state.OnError, $"{where} onError", allTransitionKeys))
            {
                yield return error;
            }

            if (state.IsFinal && state.Transitions.Count > 0)
            {
                yield return $"{where}: finish states cannot have transitions.";
            }

            if (state.Type == StateType.SubFlow)
            {
                if (state.SubFlow is null || string.IsNullOrEmpty(state.SubFlow.Workflow))
                {
                    yield return $"{where}: subFlow.workflow is required for SubFlow states.";
                }
                else
                {
                    if (!workflowExists(state.SubFlow.Workflow))
                    {
                        yield return $"{where}: subflow workflow '{state.SubFlow.Workflow}' is not defined.";
                    }

                    foreach (var (path, expression) in state.SubFlow.Input)
                    {
                        if (expressions.Validate(expression) is { } error)
                        {
                            yield return $"{where}: subFlow.input '{path}': {error}";
                        }
                    }
                }
            }

            foreach (var duplicate in state.Transitions.GroupBy(t => t.Key).Where(g => g.Count() > 1))
            {
                yield return $"{where}: transition '{duplicate.Key}' is defined more than once.";
            }

            if (state.Transitions.Count(t => t.Trigger == TriggerType.Automatic && t.Default) > 1)
            {
                yield return $"{where}: only one default automatic transition is allowed.";
            }

            foreach (var task in state.OnEntry.Concat(state.OnExit))
            {
                foreach (var error in ValidateTask(task, expressions, tasks, services, allTransitionKeys))
                {
                    yield return $"{where}: {error}";
                }
            }

            foreach (var transition in state.Transitions)
            {
                foreach (var error in ValidateTransition(transition, $"{where} transition '{transition.Key}'", stateKeys, allTransitionKeys, expressions, tasks, services))
                {
                    yield return error;
                }
            }
        }

        foreach (var shared in wf.SharedTransitions)
        {
            var where = $"shared transition '{shared.Key}'";
            if (shared.AvailableIn.Count == 0)
            {
                yield return $"{where}: availableIn is required (use \"*\" for every non-final state).";
            }

            foreach (var state in shared.AvailableIn.Where(s => s != "*" && !stateKeys.Contains(s)))
            {
                yield return $"{where}: availableIn state '{state}' does not exist.";
            }

            foreach (var error in ValidateTransition(shared, where, stateKeys, allTransitionKeys, expressions, tasks, services))
            {
                yield return error;
            }
        }
    }

    /// <summary>
    /// View names must be registered (when the host registered any views) and of the right kind:
    /// state views take <c>Instance</c>, transition views additionally take <c>Transition</c>.
    /// </summary>
    private static IEnumerable<string> ValidateViews(WorkflowDefinition wf, IReadOnlyDictionary<string, Type> views)
    {
        var references = wf.States.Where(s => s.View is not null).Select(s => (Where: $"state '{s.Key}'", View: s.View!, Transition: false))
            .Concat(wf.States.SelectMany(s => s.Transitions).Concat(wf.SharedTransitions).Append(wf.Start)
                .Where(t => t.View is not null).Select(t => (Where: $"transition '{t.Key}'", View: t.View!, Transition: true)));

        foreach (var (where, view, isTransition) in references)
        {
            if (view.Inline && !isTransition)
            {
                yield return $"{where}: view.inline is only supported on transitions.";
            }

            if (view.Layout is not null)
            {
                if (isTransition)
                {
                    yield return $"{where}: view.layout is only supported on states.";
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(view.Layout, "^[a-z0-9][a-z0-9._-]{0,99}$"))
                {
                    yield return $"{where}: view.layout '{view.Layout}' is not a valid layout key.";
                }

                if (!string.IsNullOrWhiteSpace(view.Component))
                {
                    yield return $"{where}: use either view.component or view.layout.";
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(view.Component))
            {
                if (!view.Inline)
                {
                    yield return $"{where}: view.component or view.layout is required.";
                }
            }
            else if (views.Count > 0)
            {
                if (!views.TryGetValue(view.Component, out var type))
                {
                    yield return $"{where}: view '{view.Component}' is not registered.";
                }
                else if (IsTransitionView(type) != isTransition)
                {
                    yield return $"{where}: view '{view.Component}' is a {(isTransition ? "state" : "transition")} view.";
                }
            }
        }
    }

    internal static bool IsTransitionView(Type type) => type.GetProperty("Transition") is not null;

    private static IEnumerable<string> ValidateTransition(
        TransitionDefinition t,
        string where,
        HashSet<string> stateKeys,
        HashSet<string> transitionKeys,
        WorkflowExpressionEngine expressions,
        WorkflowTaskRegistry tasks,
        IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(t.Key))
        {
            yield return $"{where}: key is required.";
        }

        if (t.Target != TransitionDefinition.Self && !stateKeys.Contains(t.Target))
        {
            yield return $"{where}: target '{t.Target}' is not a state.";
        }

        switch (t.Trigger)
        {
            case TriggerType.Automatic when !t.Default && string.IsNullOrWhiteSpace(t.Rule):
                yield return $"{where}: automatic transitions need a 'rule' (or \"default\": true).";
                break;
            case TriggerType.Scheduled:
                var hasAfter = !string.IsNullOrWhiteSpace(t.After);
                var hasAt = !string.IsNullOrWhiteSpace(t.At);
                if (!hasAfter && !hasAt)
                {
                    yield return $"{where}: scheduled transitions need a valid 'after' duration.";
                }
                else if (hasAfter && hasAt)
                {
                    yield return $"{where}: scheduled transitions cannot specify both 'after' and 'at'.";
                }
                else if (hasAfter)
                {
                    if (t.After!.Contains("{{", StringComparison.Ordinal))
                    {
                        foreach (var err in expressions.ValidateTemplate(JsonValue.Create(t.After)))
                        {
                            yield return $"{where}: after: {err}";
                        }
                    }
                    else if (!Durations.TryParse(t.After, out _))
                    {
                        yield return $"{where}: scheduled transitions need a valid 'after' duration.";
                    }
                }
                else if (hasAt)
                {
                    if (t.At!.Contains("{{", StringComparison.Ordinal))
                    {
                        foreach (var err in expressions.ValidateTemplate(JsonValue.Create(t.At)))
                        {
                            yield return $"{where}: at: {err}";
                        }
                    }
                    else if (!DateTimeOffset.TryParse(t.At, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    {
                        yield return $"{where}: scheduled transitions need a valid 'at' timestamp.";
                    }
                }

                break;
        }

        foreach (var roleEntry in t.Roles.Concat(t.DenyRoles))
        {
            if (roleEntry.Contains("{{", StringComparison.Ordinal))
            {
                foreach (var err in expressions.ValidateTemplate(JsonValue.Create(roleEntry)))
                {
                    yield return $"{where}: role '{roleEntry}': {err}";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(t.Rule) && expressions.Validate(t.Rule) is { } ruleError)
        {
            yield return $"{where}: rule: {ruleError}";
        }

        if (t.Input?.Schema is { } schema)
        {
            foreach (var error in Safely(() => JsonSchemaValidator.CheckSchema(schema)))
            {
                yield return $"{where}: input.schema {error}";
            }
        }

        foreach (var name in t.Input?.Sensitive ?? [])
        {
            // Only declared top-level properties can be stripped reliably (without a schema the body is ignored anyway).
            if (t.Input?.Schema?["properties"] is not JsonObject properties || !properties.ContainsKey(name))
            {
                yield return $"{where}: input.sensitive '{name}' is not a property of input.schema.";
            }
        }

        foreach (var (path, expression) in t.Input?.Set ?? [])
        {
            if (expressions.Validate(expression) is { } error)
            {
                yield return $"{where}: input.set '{path}': {error}";
            }
        }

        foreach (var task in t.OnExecute)
        {
            foreach (var error in ValidateTask(task, expressions, tasks, services, transitionKeys))
            {
                yield return $"{where}: {error}";
            }
        }
    }

    private static IEnumerable<string> ValidateTask(
        TaskDefinition task,
        WorkflowExpressionEngine expressions,
        WorkflowTaskRegistry tasks,
        IServiceProvider services,
        HashSet<string> transitionKeys)
    {
        if (tasks.Find(task.Task) is null)
        {
            yield return $"task '{task.DisplayKey}': unknown task type '{task.Task}'.";
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(task.When) && expressions.Validate(task.When) is { } whenError)
        {
            yield return $"task '{task.DisplayKey}': when: {whenError}";
        }

        foreach (var error in Safely(() => tasks.Resolve(services, task.Task).Validate(task, expressions)))
        {
            yield return $"task '{task.DisplayKey}': {error}";
        }

        foreach (var error in ValidatePolicy(task.OnError, $"task '{task.DisplayKey}' onError", transitionKeys))
        {
            yield return error;
        }
    }

    /// <summary>
    /// Config values of the wrong JSON type (<c>"values": { "x": 5 }</c>, <c>"pattern": 1</c>) make the validators' typed reads
    /// throw; report them as definition errors instead of crashing the loader with an unrelated exception.
    /// </summary>
    private static List<string> Safely(Func<IEnumerable<string>> validate)
    {
        try
        {
            return validate().ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return [$"invalid configuration value: {ex.Message}"];
        }
    }

    private static IEnumerable<string> ValidatePolicy(ErrorPolicy? policy, string where, HashSet<string> transitionKeys)
    {
        if (policy is null)
        {
            yield break;
        }

        if (policy.MaxAttempts < 1)
        {
            yield return $"{where}: maxAttempts must be ≥ 1.";
        }

        if (!Durations.TryParse(policy.Delay, out _))
        {
            yield return $"{where}: delay is not a valid duration.";
        }

        if (policy.MaxDelay is not null && !Durations.TryParse(policy.MaxDelay, out _))
        {
            yield return $"{where}: maxDelay is not a valid duration.";
        }

        if (policy.BackoffMultiplier < 1.0)
        {
            yield return $"{where}: backoffMultiplier must be ≥ 1.0.";
        }

        if (policy.Action == ErrorAction.Transition && (policy.Transition is null || !transitionKeys.Contains(policy.Transition)))
        {
            yield return $"{where}: transition '{policy.Transition}' is not defined in this workflow.";
        }
    }
}
