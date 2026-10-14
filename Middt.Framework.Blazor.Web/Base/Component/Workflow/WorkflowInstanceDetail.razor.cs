using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Http;
using Middt.Framework.Model.Workflow;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Component.Workflow;

/// <summary>
/// Shows one workflow instance: current state, data, the transitions the user may execute (with input forms built
/// from their JSON Schema), scheduled transitions, history and incidents.
/// </summary>
public partial class WorkflowInstanceDetail
{
    private WorkflowInstanceModel? instance;
    private WorkflowDefinitionModel? definition;
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
    }

    /// <summary>Reloads the instance, its history and incidents.</summary>
    public Task RefreshAsync() => ExecuteMethod(LoadAsync);

    private async Task LoadAsync()
    {
        if (Workflow != loadedWorkflow)
        {
            definition = null;
            loadedWorkflow = Workflow;
            var definitionResponse = await WorkflowClient.GetDefinitionAsync(Workflow, ComponentCancellation);
            definition = definitionResponse.IsSuccess ? definitionResponse.Data : null;
        }

        if (loadedId != InstanceId)
        {
            instance = null;
            history = [];
            incidents = [];
        }

        loadedId = InstanceId;
        loadFailed = false;
        if (InstanceId is not { } id)
        {
            return;
        }

        var instanceTask = WorkflowClient.GetInstanceAsync(Workflow, id, ComponentCancellation);
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
    }

    private void OpenTransition(AvailableTransitionModel transition)
    {
        transitionErrors = [];
        pendingHasInput = transition.InputSchema?["properties"] is JsonObject { Count: > 0 };
        pendingTransition = transition;
    }

    private void CloseTransition()
    {
        if (!executing)
        {
            pendingTransition = null;
        }
    }

    private async Task ExecuteTransitionAsync()
    {
        if (pendingTransition is not { } transition || instance is null)
        {
            return;
        }

        if (pendingHasInput && transitionForm is not null && !transitionForm.Validate())
        {
            return;
        }

        executing = true;
        transitionErrors = [];
        try
        {
            await ExecuteMethod(async () =>
            {
                var input = pendingHasInput ? transitionForm?.GetValue() : null;
                var response = await WorkflowClient.ExecuteTransitionAsync(Workflow, instance.Id, transition.Key, input, cancellationToken: ComponentCancellation);
                if (!response.IsSuccess || response.Data is null)
                {
                    transitionErrors = response.MessageList.Count > 0 ? response.MessageList : ["İşlem uygulanamadı."];
                    Notification.ShowErrorMessage($"{transition.Title ?? transition.Key} uygulanamadı", string.Join(" ", transitionErrors));
                    return;
                }

                pendingTransition = null;
                Notification.ShowSuccessMessage(
                    $"{transition.Title ?? transition.Key} uygulandı",
                    $"Yeni adım: {response.Data.StateTitle ?? response.Data.State}");
                await LoadAsync();
                await OnChanged.InvokeAsync(instance ?? response.Data);
            });
        }
        finally
        {
            executing = false;
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

    private string StateTitle(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return "Başlangıç";
        }

        return definition?.States.FirstOrDefault(s => s.Key == state)?.Title ?? state;
    }

    private string TransitionTitle(string key) =>
        definition?.Transitions.FirstOrDefault(t => t.Key == key && !string.IsNullOrEmpty(t.Title))?.Title ?? key;

    private static string TriggerText(string trigger) => trigger switch
    {
        "Manual" => "Kullanıcı",
        "Automatic" => "Otomatik",
        "Scheduled" => "Zamanlanmış",
        "SubFlow" => "Alt akış",
        "Start" => "Başlangıç",
        "Timeout" => "Zaman aşımı",
        _ => trigger,
    };

    private string? TargetSubType(AvailableTransitionModel transition) =>
        definition?.States.FirstOrDefault(s => s.Key == transition.Target)?.SubType;

    private ButtonStyle TransitionStyle(AvailableTransitionModel transition) => TargetSubType(transition) switch
    {
        "Success" => ButtonStyle.Success,
        "Error" => ButtonStyle.Danger,
        "Cancelled" or "Timeout" => ButtonStyle.Light,
        _ => ButtonStyle.Primary,
    };

    private string TransitionIcon(AvailableTransitionModel transition) => TargetSubType(transition) switch
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
