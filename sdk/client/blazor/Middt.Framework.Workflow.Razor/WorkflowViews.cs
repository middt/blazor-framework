using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Middt.Framework.Model.Workflow;

namespace Middt.Framework.Workflow.Razor;

/// <summary>
/// Registers a Razor component as a workflow view under <paramref name="name"/>. Workflow definitions reference it with
/// <c>"view": { "component": "name" }</c> on a state (screen for the instance) or on a transition (input form).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WorkflowViewAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Base class of state views. The same component is rendered interactively in Blazor (inside <c>WorkflowInstanceDetail</c>)
/// and as static HTML by the workflow API (<c>GET …/instances/{id}/view</c>), so keep it free of JS interop and only
/// output data through Razor expressions (automatically HTML encoded) – never <see cref="MarkupString"/> with instance data.
/// </summary>
public abstract class WorkflowViewBase : ComponentBase
{
    [Parameter, EditorRequired]
    public WorkflowInstanceModel Instance { get; set; } = default!;

    /// <summary><c>false</c> when rendered to static HTML by the API.</summary>
    [Parameter]
    public bool Interactive { get; set; }

    protected JsonObject Data => Instance.Data;

    /// <summary>Value at a dot path (<c>credit.score</c>) as display text, or <paramref name="fallback"/>.</summary>
    protected string Text(string path, string fallback = "-") =>
        WorkflowViewData.Get(Data, path) switch
        {
            null => fallback,
            JsonValue value => Convert.ToString(value.GetValue<object>(), CultureInfo.CurrentCulture) ?? fallback,
            var node => node.ToJsonString(),
        };

    protected decimal? Number(string path) =>
        WorkflowViewData.Get(Data, path) is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;

    protected DateTimeOffset? Date(string path) =>
        WorkflowViewData.Get(Data, path) is JsonValue value && value.TryGetValue<DateTimeOffset>(out var date) ? date : null;
}

/// <summary>
/// Base class of transition views: a custom input form that replaces the schema generated form.
/// Call <see cref="SubmitAsync"/> with the request body; validation errors from the API come back in <see cref="Errors"/>.
/// </summary>
public abstract class WorkflowTransitionViewBase : ComponentBase
{
    [Parameter, EditorRequired]
    public WorkflowInstanceModel Instance { get; set; } = default!;

    [Parameter, EditorRequired]
    public AvailableTransitionModel Transition { get; set; } = default!;

    [Parameter]
    public EventCallback<JsonObject> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public IReadOnlyList<string> Errors { get; set; } = [];

    [Parameter]
    public bool Busy { get; set; }

    protected JsonObject Data => Instance.Data;

    protected Task SubmitAsync(JsonObject body) => OnSubmit.InvokeAsync(body);

    protected Task CancelAsync() => OnCancel.InvokeAsync();
}

/// <summary>Name → component type map of the views of one or more assemblies.</summary>
public sealed class WorkflowViewCatalog
{
    private readonly Dictionary<string, Type> views = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Type> Views => views;

    public static WorkflowViewCatalog FromAssemblyOf<T>() => new WorkflowViewCatalog().AddAssembly(typeof(T).Assembly);

    public WorkflowViewCatalog AddAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false } && typeof(IComponent).IsAssignableFrom(t)))
        {
            if (type.GetCustomAttribute<WorkflowViewAttribute>() is { } attribute)
            {
                Add(attribute.Name, type);
            }
        }

        return this;
    }

    public WorkflowViewCatalog Add(string name, Type componentType)
    {
        if (!typeof(WorkflowViewBase).IsAssignableFrom(componentType) && !typeof(WorkflowTransitionViewBase).IsAssignableFrom(componentType))
        {
            throw new ArgumentException($"{componentType.Name} must derive from {nameof(WorkflowViewBase)} or {nameof(WorkflowTransitionViewBase)}.", nameof(componentType));
        }

        if (!views.TryAdd(name, componentType) && views[name] != componentType)
        {
            throw new InvalidOperationException($"Workflow view '{name}' is registered twice ({views[name].Name}, {componentType.Name}).");
        }

        return this;
    }

    public Type? Find(string? name) => name is not null && views.TryGetValue(name, out var type) ? type : null;

    public bool IsStateView(string name) => Find(name) is { } type && typeof(WorkflowViewBase).IsAssignableFrom(type);

    public bool IsTransitionView(string name) => Find(name) is { } type && typeof(WorkflowTransitionViewBase).IsAssignableFrom(type);
}

internal static class WorkflowViewData
{
    public static JsonNode? Get(JsonObject data, string path)
    {
        JsonNode? current = data;
        foreach (var segment in path.Split('.'))
        {
            current = current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next) ? next : null;
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}
