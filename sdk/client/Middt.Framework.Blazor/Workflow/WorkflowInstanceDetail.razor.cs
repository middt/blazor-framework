using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Workflow;
using Radzen;

namespace Middt.Framework.Blazor.Workflow;

/// <summary>
/// Shows one workflow instance: current state, data, the transitions the user may execute (with input forms built
/// from their JSON Schema), scheduled transitions, history and incidents.
/// Wizard workflows (states with a <see cref="WorkflowStateModel.Step"/>) get a step indicator, and a transition marked
/// <see cref="AvailableTransitionModel.Inline"/> is rendered as the step form inside the screen instead of a dialog.
/// </summary>
public partial class WorkflowInstanceDetail
{
    private WorkflowInstanceModel? instance;
    private WorkflowDefinitionModel? definition;
    private readonly Dictionary<int, WorkflowDefinitionModel> definitions = [];
    private List<WorkflowHistoryModel> history = [];
    private List<WorkflowIncidentModel> incidents = [];
    private string? loadedWorkflow;
    private Guid? loadedId;
    private bool loadFailed;
    private bool retrying;

    private AvailableTransitionModel? pendingTransition;
    private bool pendingHasInput;
    private JsonSchemaForm? transitionForm;
    private List<string> transitionErrors = [];
    private bool executing;

    // Wizard step form (inline transition)
    private AvailableTransitionModel? stepTransition;
    private JsonObject? stepPrefill;

    /// <summary>Initial value of the transition dialog form (the edited data of a <c>jsonPatch</c> transition).</summary>
    private JsonObject? pendingPrefill;
    private JsonSchemaForm? stepForm;
    private ElementReference stateHeading;
    private List<string> stepErrors = [];
    private bool focusAfterRender;
    private bool polling;

    private TimeZoneInfo? timeZone;

    [Inject]
    private WorkflowApiClient WorkflowClient { get; set; } = default!;

    /// <summary>Workflow key (e.g. <c>order-approval</c>).</summary>
    [Parameter, EditorRequired]
    public string Workflow { get; set; } = string.Empty;

    /// <summary>Instance to show; <c>null</c> shows a hint to select one.</summary>
    [Parameter]
    public Guid? InstanceId { get; set; }

    /// <summary>Raised after a transition or retry changed the instance.</summary>
    [Parameter]
    public EventCallback<WorkflowInstanceModel> OnChanged { get; set; }

    /// <summary>Prefix of element ids (unique per page when several details are shown).</summary>
    [Parameter]
    public string IdPrefix { get; set; } = "wf";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (IsFirstRenderCompleted && (InstanceId != loadedId || Workflow != loadedWorkflow))
        {
            await RefreshAsync();
        }
    }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            timeZone = await WorkflowUi.GetBrowserTimeZoneAsync(JsRuntime, ComponentCancellation);
        }

        if (firstRender && InstanceId is not null)
        {
            await RefreshAsync();
        }

        if (focusAfterRender && instance is not null && !executing)
        {
            focusAfterRender = false;
            await FocusStepAsync();
        }
    }

    /// <summary>After a step was submitted: focus the first field of the next step form (or the state heading) for keyboard users.</summary>
    private async Task FocusStepAsync()
    {
        try
        {
            if (stepTransition is not null && stepForm is { HasFields: true })
            {
                await stepForm.FocusFirstAsync();
            }
            else
            {
                await stateHeading.FocusAsync();
            }
        }
        catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or InvalidOperationException or TaskCanceledException)
        {
            // element not rendered (anymore); focus is only a convenience
        }
    }

    /// <summary>Reloads the instance, its history and incidents.</summary>
    public Task RefreshAsync() => ExecuteMethod(() => LoadAsync());

    /// <param name="loaded">The instance when the caller already fetched it (polling); only history and incidents are loaded then.</param>
    private async Task LoadAsync(WorkflowInstanceModel? loaded = null)
    {
        if (Workflow != loadedWorkflow)
        {
            definition = null;
            definitions.Clear();
            loadedWorkflow = Workflow;
        }

        if (loadedId != InstanceId)
        {
            instance = null;
            history = [];
            incidents = [];
            stepTransition = null;
            stepPrefill = null;
            stepErrors = [];
        }

        loadedId = InstanceId;
        loadFailed = false;
        if (InstanceId is not { } id)
        {
            return;
        }

        var instanceTask = loaded?.Id == id
            ? Task.FromResult(BaseResponseDataModel<WorkflowInstanceModel>.Success(loaded))
            : WorkflowClient.GetInstanceAsync(Workflow, id, ComponentCancellation);
        var historyTask = WorkflowClient.GetHistoryAsync(Workflow, id, ComponentCancellation);
        var incidentsTask = WorkflowClient.GetIncidentsAsync(Workflow, id, ComponentCancellation);
        await Task.WhenAll(instanceTask, historyTask, incidentsTask);

        var instanceResponse = await instanceTask;
        if (!instanceResponse.IsSuccess || instanceResponse.Data is null)
        {
            instance = null;
            loadFailed = true;
            Notification.ShowErrorMessage("Kayıt yüklenemedi", instanceResponse.ErrorText);
            return;
        }

        instance = instanceResponse.Data;
        var historyResponse = await historyTask;
        history = historyResponse.IsSuccess ? historyResponse.Data ?? [] : [];
        var incidentsResponse = await incidentsTask;
        incidents = incidentsResponse.IsSuccess ? incidentsResponse.Data ?? [] : [];

        // Instances keep running on the definition version they were started with.
        definition = await GetDefinitionAsync(instance.WorkflowVersion);

        var inline = instance.Status == WorkflowInstanceStatus.Active && instance.WaitingForInstanceId is null
            ? instance.AvailableTransitions.FirstOrDefault(t => t.Inline)
            : null;
        stepPrefill = inline is null ? null : WorkflowUi.StepPrefill(instance.Data, inline.InputInto, inline.InputSchema);
        stepTransition = inline;
        stepErrors = [];
    }

    private async Task<WorkflowDefinitionModel?> GetDefinitionAsync(int version)
    {
        if (definitions.TryGetValue(version, out var cached))
        {
            return cached;
        }

        var response = await WorkflowClient.GetDefinitionAsync(Workflow, version, ComponentCancellation);
        if (!response.IsSuccess || response.Data is null)
        {
            return null;
        }

        definitions[version] = response.Data;
        return response.Data;
    }

    private void OpenTransition(AvailableTransitionModel transition)
    {
        transitionErrors = [];
        pendingHasInput = transition.InputSchema?["properties"] is JsonObject { Count: > 0 };
        pendingPrefill = WorkflowUi.TransitionPrefill(instance?.Data, transition);
        pendingTransition = transition;
    }

    private void CloseTransition()
    {
        if (!executing)
        {
            pendingTransition = null;
        }
    }

    /// <summary>
    /// Variables of a state's JSON layout: <c>data</c> (instance data) and <c>instance</c> (id, key, state, workflow, status).
    /// Cached while the shown values are unchanged: a new dictionary would rebuild the layout's render context and re-render
    /// (and reload) every grid and select of the embedded layout on each render of this component.
    /// </summary>
    private IReadOnlyDictionary<string, JsonNode?> LayoutVariables
    {
        get
        {
            var key = instance is null ? default(LayoutVariablesKey?)
                : new LayoutVariablesKey(instance.Id, instance.DataVersion, instance.Layout, instance.Key, instance.State, instance.StateTitle, instance.Workflow, instance.Status, instance.StartedBy);
            if (layoutVariables is null || layoutVariablesKey != key)
            {
                layoutVariablesKey = key;
                availableTransitionKeys = instance?.AvailableTransitions.Select(t => t.Key).ToHashSet(StringComparer.Ordinal) ?? [];
                layoutVariables = new Dictionary<string, JsonNode?>
                {
                    ["data"] = instance?.Data.DeepClone(),
                    ["instance"] = instance is null ? null : new JsonObject
                    {
                        ["id"] = instance.Id.ToString(),
                        ["key"] = instance.Key,
                        ["state"] = instance.State,
                        ["stateTitle"] = instance.StateTitle,
                        ["workflow"] = instance.Workflow,
                        ["status"] = instance.Status.ToString(),
                        ["startedBy"] = instance.StartedBy,
                    },
                };
            }

            return layoutVariables;
        }
    }

    private IReadOnlySet<string> AvailableTransitionKeys
    {
        get
        {
            _ = LayoutVariables;
            return availableTransitionKeys ?? new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private Task ExecuteLayoutTransitionAsync(string transitionKey, JsonObject? input)
    {
        if (executing || instance?.AvailableTransitions.FirstOrDefault(t => t.Key == transitionKey) is not { } transition)
        {
            return Task.CompletedTask;
        }

        return ExecuteTransitionCoreAsync(transition, input, fromStep: false);
    }

    private IReadOnlyDictionary<string, JsonNode?>? layoutVariables;
    private HashSet<string>? availableTransitionKeys;
    private LayoutVariablesKey? layoutVariablesKey;

    private readonly record struct LayoutVariablesKey(
        Guid Id, int DataVersion, string? Layout, string? Key, string State, string? StateTitle, string Workflow, WorkflowInstanceStatus Status, string? StartedBy);

    /// <summary>Whether a step form is rendered inside the screen (wizard).</summary>
    private bool ShowStepForm => stepTransition is not null && instance is not null;

    /// <summary>Transitions shown as buttons: all available ones except the inline step transition.</summary>
    private IEnumerable<AvailableTransitionModel> ButtonTransitions =>
        instance?.AvailableTransitions.Where(t => !ReferenceEquals(t, stepTransition)) ?? [];

    /// <summary>Next to a step form, transitions without input (e.g. "Geri") run directly; others open their dialog.</summary>
    private bool RunsDirectly(AvailableTransitionModel transition) => ShowStepForm && !HasInput(transition);

    private static bool HasInput(AvailableTransitionModel transition) =>
        transition.InputSchema?["properties"] is JsonObject { Count: > 0 };

    private async Task SubmitStepAsync()
    {
        if (stepTransition is not { } transition || executing)
        {
            return;
        }

        var hasInput = HasInput(transition);
        if (hasInput && stepForm is not null && !stepForm.Validate())
        {
            return;
        }

        await ExecuteTransitionCoreAsync(transition, hasInput ? stepForm?.GetValue() : null, fromStep: true);
    }

    private Task OnTransitionClick(AvailableTransitionModel transition)
    {
        if (RunsDirectly(transition))
        {
            return executing ? Task.CompletedTask : ExecuteTransitionCoreAsync(transition, null, fromStep: true);
        }

        OpenTransition(transition);
        return Task.CompletedTask;
    }

    private async Task ExecuteTransitionAsync()
    {
        if (pendingTransition is null || instance is null)
        {
            return;
        }

        if (pendingHasInput && transitionForm is not null && !transitionForm.Validate())
        {
            return;
        }

        await ExecuteTransitionCoreAsync(pendingTransition, pendingHasInput ? transitionForm?.GetValue() : null, fromStep: false);
    }

    /// <summary>
    /// Executes <paramref name="transition"/>. <paramref name="fromStep"/>: started from the wizard step form (inline submit or
    /// a direct "Geri"); errors are shown inside the step form (entered values stay) instead of the dialog.
    /// </summary>
    private async Task ExecuteTransitionCoreAsync(AvailableTransitionModel transition, JsonObject? input, bool fromStep)
    {
        if (instance is null)
        {
            return;
        }

        executing = true;
        transitionErrors = [];
        stepErrors = [];
        try
        {
            await ExecuteMethod(async () =>
            {
                var body = WorkflowUi.TransitionInput(instance.Data, transition, input);
                var response = await WorkflowClient.ExecuteTransitionAsync(Workflow, instance.Id, transition.Key, body, cancellationToken: ComponentCancellation);
                if (!response.IsSuccess || response.Data is null)
                {
                    var errors = response.MessageList.Count > 0 ? response.MessageList : ["İşlem uygulanamadı."];
                    if (fromStep)
                    {
                        stepErrors = errors;
                    }
                    else
                    {
                        transitionErrors = errors;
                        Notification.ShowErrorMessage($"{transition.Title ?? transition.Key} uygulanamadı", string.Join(" ", errors));
                    }

                    return;
                }

                pendingTransition = null;
                if (!fromStep)
                {
                    Notification.ShowSuccessMessage(
                        $"{transition.Title ?? transition.Key} uygulandı",
                        $"Yeni adım: {response.Data.StateTitle ?? response.Data.State}");
                }

                await LoadAsync();
                focusAfterRender = fromStep;
                await OnChanged.InvokeAsync(instance ?? response.Data);
            });
        }
        finally
        {
            executing = false;
        }

        StartPollingIfPending();
    }

    /// <summary>
    /// While the instance is busy or waits for a subflow (e.g. right after a step was submitted), reloads it a few times so
    /// the next step appears without the user pressing "Yenile".
    /// </summary>
    private void StartPollingIfPending()
    {
        if (polling || !IsPending(instance))
        {
            return;
        }

        polling = true;
        _ = PollAsync(instance!.Id);
    }

    private static bool IsPending(WorkflowInstanceModel? model) =>
        model is not null && (model.Status == WorkflowInstanceStatus.Busy
            || (model.Status == WorkflowInstanceStatus.Active && model.WaitingForInstanceId is not null));

    private static bool Changed(WorkflowInstanceModel current, WorkflowInstanceModel polled) =>
        current.State != polled.State || current.Status != polled.Status || current.DataVersion != polled.DataVersion
        || current.WaitingForInstanceId != polled.WaitingForInstanceId;

    private async Task PollAsync(Guid id)
    {
        try
        {
            for (var attempt = 0; attempt < 15; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ComponentCancellation);
                if (instance?.Id != id || !IsPending(instance))
                {
                    return;
                }

                if (executing)
                {
                    continue;
                }

                // Only the instance is polled; history and incidents are loaded once it changed (normally: when it settles).
                var response = await WorkflowClient.GetInstanceAsync(Workflow, id, ComponentCancellation);
                if (!response.IsSuccess || response.Data is not { } polled)
                {
                    continue;
                }

                await InvokeAsync(async () =>
                {
                    if (instance?.Id != id || executing || (IsPending(polled) && !Changed(instance, polled)))
                    {
                        return;
                    }

                    await LoadAsync(polled);
                    if (!IsPending(instance))
                    {
                        focusAfterRender = true;
                        await OnChanged.InvokeAsync(instance);
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // component disposed
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Polling workflow instance {Id} failed", id);
        }
        finally
        {
            polling = false;
        }
    }

    private async Task RetryAsync()
    {
        if (instance is null)
        {
            return;
        }

        retrying = true;
        try
        {
            await ExecuteMethod(async () =>
            {
                var response = await WorkflowClient.RetryAsync(Workflow, instance.Id, ComponentCancellation);
                if (!response.IsSuccess || response.Data is null)
                {
                    Notification.ShowErrorMessage("Tekrar deneme başarısız", response.ErrorText);
                    return;
                }

                Notification.ShowSuccessMessage("Tekrar denendi", $"Durum: {WorkflowUi.StatusText(response.Data.Status)}");
                await LoadAsync();
                await OnChanged.InvokeAsync(instance ?? response.Data);
            });
        }
        finally
        {
            retrying = false;
        }
    }

    /// <summary>Wizard steps (states grouped by <see cref="WorkflowStateModel.Step"/>); empty when the definition has none.</summary>
    private List<WizardStep> Steps
    {
        get
        {
            if (definition is null || instance is null || !definition.States.Any(s => s.Step is not null))
            {
                return [];
            }

            // Most recently entered state per key (history is not guaranteed to be ordered).
            var visited = history
                .GroupBy(h => h.ToState, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Max(h => h.FinishedAt), StringComparer.Ordinal);
            var stepOf = definition.States.Where(s => s.Step is not null).ToDictionary(s => s.Key, s => s.Step!.Value, StringComparer.Ordinal);

            int? current = stepOf.TryGetValue(instance.State, out var step) ? step : null;
            var finished = instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled;

            // Not on a step (e.g. a subflow or routing state, cancelled): everything up to the last visited step is done.
            var reached = current ?? visited.Keys.Where(stepOf.ContainsKey).Select(k => stepOf[k]).DefaultIfEmpty(0).Max();

            return definition.States
                .Where(s => s.Step is not null)
                .GroupBy(s => s.Step!.Value)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var states = g.ToList();
                    var shown = states.FirstOrDefault(s => s.Key == instance.State)
                        ?? states.Where(s => visited.ContainsKey(s.Key)).OrderByDescending(s => visited[s.Key]).FirstOrDefault();
                    var label = shown is not null
                        ? shown.Title ?? shown.Key
                        : string.Join(" / ", states.Select(s => s.Title ?? s.Key).Distinct(StringComparer.Ordinal));
                    var isCurrent = g.Key == current;
                    var completed = isCurrent
                        ? finished
                        : g.Key < reached || (current is null && g.Key == reached) || states.Any(s => visited.ContainsKey(s.Key));
                    return new WizardStep(g.Key, label, isCurrent, completed);
                })
                .ToList();
        }
    }

    private static string StepClass(WizardStep step) =>
        "middt-wf-step" + (step.Current ? " middt-wf-step-current" : null) + (step.Completed ? " middt-wf-step-done" : null)
        + (!step.Current && !step.Completed ? " middt-wf-step-upcoming" : null);

    private static string StepStatusText(WizardStep step) => step.Current
        ? (step.Completed ? "tamamlandı, şu anki adım" : "şu anki adım")
        : step.Completed ? "tamamlandı" : "sıradaki adım";

    private sealed record WizardStep(int Number, string Label, bool Current, bool Completed);

    private string StateTitle(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return "Başlangıç";
        }

        return definition?.States.FirstOrDefault(s => s.Key == state)?.Title ?? state;
    }

    private string TransitionTitle(string key) =>
        definition?.Transitions.FirstOrDefault(t => t.Key == key && !string.IsNullOrEmpty(t.Title))?.Title
            ?? (key == "$background" ? "Arka plan görevi" : key);

    private static string TriggerText(string trigger) => trigger switch
    {
        "Manual" => "Kullanıcı",
        "Automatic" => "Otomatik",
        "Scheduled" => "Zamanlanmış",
        "SubFlow" => "Alt akış",
        "Start" => "Başlangıç",
        "Timeout" => "Zaman aşımı",
        "Event" => "Mesaj",
        "Background" => "Arka plan görevi",
        _ => trigger,
    };

    private string? TargetSubType(AvailableTransitionModel transition) =>
        definition?.States.FirstOrDefault(s => s.Key == transition.Target)?.SubType;

    private ButtonStyle TransitionStyle(AvailableTransitionModel transition) =>
        transition.Key.StartsWith("delete-", StringComparison.Ordinal)
            ? ButtonStyle.Danger
            : TargetSubType(transition) switch
            {
                "Success" => ButtonStyle.Success,
                "Error" => ButtonStyle.Danger,
                "Cancelled" or "Timeout" => ButtonStyle.Light,
                _ => ButtonStyle.Primary,
            };

    private string TransitionIcon(AvailableTransitionModel transition) =>
        transition.Key.StartsWith("delete-", StringComparison.Ordinal)
            ? "delete"
            : TargetSubType(transition) switch
            {
                "Success" => "check_circle",
                "Error" => "block",
                "Cancelled" or "Timeout" => "cancel",
                _ => "arrow_forward",
            };

    private PointStyle HistoryPointStyle(WorkflowHistoryModel entry) =>
        definition?.States.FirstOrDefault(s => s.Key == entry.ToState)?.SubType switch
        {
            "Success" => PointStyle.Success,
            "Error" => PointStyle.Danger,
            "Cancelled" or "Timeout" => PointStyle.Secondary,
            _ => PointStyle.Primary,
        };
}
