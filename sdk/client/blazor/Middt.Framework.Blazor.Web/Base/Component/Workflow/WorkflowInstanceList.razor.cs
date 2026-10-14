using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Middt.Framework.Client.Http;
using Middt.Framework.Model.Workflow;
using Radzen;
using Radzen.Blazor;

namespace Middt.Framework.Blazor.Web.Base.Component.Workflow;

/// <summary>
/// Server-paged list of the instances of a workflow with a "start new" dialog built from the start input schema.
/// Workflows whose start takes no input (e.g. wizards that collect the data in their first step) start immediately.
/// </summary>
public partial class WorkflowInstanceList
{
    private RadzenDataGrid<WorkflowInstanceModel> grid = default!;
    private JsonSchemaForm? startForm;
    private WorkflowDefinitionModel? definition;
    private List<WorkflowInstanceModel> items = [];
    private IList<WorkflowInstanceModel> selection = [];
    private int count;
    private bool loading;
    private int page = 1;
    private int? pageSize;
    private WorkflowInstanceStatus? statusFilter;
    private string? loadedWorkflow;
    private bool resettingPager;
    private Guid? selectedId;
    private Guid? lastSelectedIdParameter;

    private bool startOpen;
    private bool starting;
    private string? startKey;
    private List<string> startErrors = [];

    private TimeZoneInfo? timeZone;

    [Inject]
    private WorkflowApiClient WorkflowClient { get; set; } = default!;

    /// <summary>Workflow key (e.g. <c>order-approval</c>).</summary>
    [Parameter, EditorRequired]
    public string Workflow { get; set; } = string.Empty;

    /// <summary>Heading; defaults to the workflow title.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>Raised when the user selects an instance (or after starting one).</summary>
    [Parameter]
    public EventCallback<WorkflowInstanceModel> OnSelected { get; set; }

    /// <summary>Only list instances in this state.</summary>
    [Parameter]
    public string? State { get; set; }

    /// <summary>Initial status filter.</summary>
    [Parameter]
    public WorkflowInstanceStatus? Status { get; set; }

    [Parameter]
    public bool ShowStatusFilter { get; set; } = true;

    /// <summary>Shows the "Yeni başlat" button.</summary>
    [Parameter]
    public bool AllowStart { get; set; } = true;

    [Parameter]
    public int PageSize { get; set; } = 10;

    /// <summary>Prefix of element ids (unique per page when several lists are shown).</summary>
    [Parameter]
    public string IdPrefix { get; set; } = "wf";

    /// <summary>Id of the instance highlighted in the grid.</summary>
    [Parameter]
    public Guid? SelectedId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (IsFirstRenderCompleted && loadedWorkflow != Workflow)
        {
            await LoadAllAsync();
        }

        if (SelectedId != lastSelectedIdParameter)
        {
            lastSelectedIdParameter = SelectedId;
            selectedId = SelectedId;
        }

        SyncSelection();
    }

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            statusFilter = Status;
            timeZone = await WorkflowUi.GetBrowserTimeZoneAsync(JsRuntime, ComponentCancellation);
            await LoadAllAsync();
        }
    }

    private int CurrentPageSize => pageSize ?? PageSize;

    /// <summary>Reloads the current page.</summary>
    public Task RefreshAsync() => ExecuteMethod(LoadPageAsync);

    private Task LoadAllAsync() => ExecuteMethod(async () =>
    {
        loadedWorkflow = Workflow;
        page = 1;
        var response = await WorkflowClient.GetDefinitionAsync(Workflow, cancellationToken: ComponentCancellation);
        if (response.IsSuccess)
        {
            definition = response.Data;
        }
        else
        {
            definition = null;
            Notification.ShowErrorMessage("Akış tanımı alınamadı", response.ErrorText);
        }

        await LoadPageAsync();
    });

    private async Task LoadPageAsync()
    {
        loading = true;
        try
        {
            var response = await WorkflowClient.ListInstancesAsync(Workflow, State, statusFilter, null, page, CurrentPageSize, ComponentCancellation);
            if (response.IsSuccess)
            {
                items = response.Data ?? [];
                count = response.Count;
            }
            else
            {
                items = [];
                count = 0;
                Notification.ShowErrorMessage("Kayıtlar alınamadı", response.ErrorText);
            }

            SyncSelection();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        // The grid asks for data while initialising; the first load is driven by CustomOnAfterRenderAsync.
        if (!IsFirstRenderCompleted || loading || resettingPager)
        {
            return;
        }

        var top = args.Top is > 0 ? args.Top.Value : CurrentPageSize;
        pageSize = top;
        page = ((args.Skip ?? 0) / top) + 1;
        await ExecuteMethod(LoadPageAsync);
    }

    private async Task OnStatusFilterChanged(WorkflowInstanceStatus? value)
    {
        statusFilter = value;
        await ResetPagerAsync();
        await ExecuteMethod(LoadPageAsync);
    }

    private async Task OnRowSelect(WorkflowInstanceModel item)
    {
        selectedId = item.Id;
        selection = [item];
        await OnSelected.InvokeAsync(item);
    }

    private void SyncSelection()
    {
        selection = selectedId is { } id && items.FirstOrDefault(i => i.Id == id) is { } item ? [item] : [];
    }

    private async Task ResetPagerAsync()
    {
        page = 1;
        if (grid is null)
        {
            return;
        }

        resettingPager = true;
        try
        {
            await grid.FirstPage();
        }
        finally
        {
            resettingPager = false;
        }
    }

    /// <summary><c>true</c> when the start transition declares input fields (the start dialog is needed).</summary>
    private bool StartHasInput => definition?.StartInputSchema?["properties"] is JsonObject { Count: > 0 };

    private Task OnStartClick()
    {
        if (StartHasInput)
        {
            OpenStartDialog();
            return Task.CompletedTask;
        }

        // Nothing to ask: start right away and select the new instance (its first step is shown by the detail).
        startKey = null;
        startErrors = [];
        return StartAsync();
    }

    private void OpenStartDialog()
    {
        startKey = null;
        startErrors = [];
        startForm?.Reset();
        startOpen = true;
    }

    private void CloseStartDialog()
    {
        if (!starting)
        {
            startOpen = false;
        }
    }

    private async Task StartAsync()
    {
        if (starting || (startOpen && startForm is not null && !startForm.Validate()))
        {
            return;
        }

        starting = true;
        startErrors = [];
        try
        {
            await ExecuteMethod(async () =>
            {
                var request = new StartWorkflowRequest
                {
                    Key = string.IsNullOrWhiteSpace(startKey) ? null : startKey.Trim(),
                    Data = startOpen ? startForm?.GetValue() ?? [] : [],
                };

                var response = await WorkflowClient.StartAsync(Workflow, request, ComponentCancellation);
                if (!response.IsSuccess || response.Data is null)
                {
                    startErrors = response.MessageList.Count > 0 ? response.MessageList : ["Akış başlatılamadı."];
                    if (!startOpen)
                    {
                        Notification.ShowErrorMessage("Akış başlatılamadı", string.Join(" ", startErrors));
                    }

                    return;
                }

                startOpen = false;
                Notification.ShowSuccessMessage("Akış başlatıldı", $"{response.Data.StateTitle ?? response.Data.State} ({WorkflowUi.StatusText(response.Data.Status)})");
                selectedId = response.Data.Id;
                await ResetPagerAsync();
                await LoadPageAsync();
                await OnSelected.InvokeAsync(response.Data);
            });
        }
        finally
        {
            starting = false;
        }
    }
}
