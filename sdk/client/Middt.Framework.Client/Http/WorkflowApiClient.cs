using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Client.DependencyInjection;

namespace Middt.Framework.Client.Http;

/// <summary>
/// Typed client for the workflow REST API mapped by <c>MapMiddtWorkflows</c> (default prefix <c>api/workflows</c>).
/// Register with <c>services.AddMiddtApiClient&lt;WorkflowApiClient&gt;()</c>. Failures (ProblemDetails 400/403/404/409,
/// network errors) come back as <c>Result = Error</c> with the server messages in <see cref="BaseResponseModel.MessageList"/>.
/// </summary>
public class WorkflowApiClient(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<WorkflowApiClient> logger)
    : ApiClientBase(httpClient, sessionState, tokenService, logger)
{
    /// <summary>Route prefix the workflow endpoints are mapped under.</summary>
    protected virtual string RoutePrefix => "api/workflows";

    /// <summary>API settings (e.g. for derived clients that build versioned prefixes).</summary>
    protected ApiSettings ApiSettings => apiSettings.Value;

    public virtual Task<BaseResponseDataModel<List<WorkflowSummaryModel>>> ListWorkflowsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<WorkflowSummaryModel>>>(HttpMethod.Get, RoutePrefix, null, cancellationToken);

    /// <summary>Gets the definition of <paramref name="workflow"/>: the latest version, or <paramref name="version"/> when given
    /// (instances keep running on the version they were started with, see <see cref="WorkflowInstanceModel.WorkflowVersion"/>).</summary>
    public virtual Task<BaseResponseDataModel<WorkflowDefinitionModel>> GetDefinitionAsync(string workflow, int? version = null, CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder();
        AppendQuery(query, "version", version?.ToString(CultureInfo.InvariantCulture));
        return SendAsync<BaseResponseDataModel<WorkflowDefinitionModel>>(HttpMethod.Get, $"{WorkflowRoute(workflow)}{query}", null, cancellationToken);
    }

    /// <summary>Starts an instance. Starting twice with the same business key returns the existing instance.</summary>
    public virtual Task<BaseResponseDataModel<WorkflowInstanceModel>> StartAsync(string workflow, StartWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<BaseResponseDataModel<WorkflowInstanceModel>>(HttpMethod.Post, $"{WorkflowRoute(workflow)}/instances", request, cancellationToken);
    }

    /// <summary>Lists instances (one page); <see cref="BaseResponseDataModel{T}.Count"/> is the total count.</summary>
    public virtual Task<BaseResponseDataModel<List<WorkflowInstanceModel>>> ListInstancesAsync(
        string workflow,
        string? state = null,
        WorkflowInstanceStatus? status = null,
        string? key = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ListInstancesAsync(workflow, state, status, key, page, pageSize, null, cancellationToken);

    /// <summary>Lists instances (one page) with optional workflow data property filters; <see cref="BaseResponseDataModel{T}.Count"/> is the total count.</summary>
    public virtual Task<BaseResponseDataModel<List<WorkflowInstanceModel>>> ListInstancesAsync(
        string workflow,
        string? state,
        WorkflowInstanceStatus? status,
        string? key,
        int page,
        int pageSize,
        IReadOnlyDictionary<string, string?>? filter,
        CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder();
        AppendQuery(query, "state", state);
        AppendQuery(query, "status", status?.ToString());
        AppendQuery(query, "key", key);
        if (filter is not null)
        {
            foreach (var (field, value) in filter)
            {
                if (!string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value))
                {
                    AppendQuery(query, $"data.{field.Trim()}", value.Trim());
                }
            }
        }

        AppendQuery(query, "page", page.ToString(CultureInfo.InvariantCulture));
        AppendQuery(query, "pageSize", pageSize.ToString(CultureInfo.InvariantCulture));
        return SendAsync<BaseResponseDataModel<List<WorkflowInstanceModel>>>(HttpMethod.Get, $"{WorkflowRoute(workflow)}/instances{query}", null, cancellationToken);
    }

    /// <summary>Gets an instance including the transitions the current user may execute.</summary>
    public virtual Task<BaseResponseDataModel<WorkflowInstanceModel>> GetInstanceAsync(string workflow, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<WorkflowInstanceModel>>(HttpMethod.Get, InstanceRoute(workflow, id), null, cancellationToken);

    /// <summary>Executes a manual transition. With <paramref name="runAsync"/> the transition is queued (202).</summary>
    public virtual Task<BaseResponseDataModel<WorkflowInstanceModel>> ExecuteTransitionAsync(
        string workflow,
        Guid id,
        string transition,
        JsonObject? input = null,
        bool runAsync = false,
        CancellationToken cancellationToken = default)
    {
        var uri = $"{InstanceRoute(workflow, id)}/transitions/{EscapePathSegment(transition, nameof(transition))}";
        if (runAsync)
        {
            uri += "?async=true";
        }

        return SendAsync<BaseResponseDataModel<WorkflowInstanceModel>>(HttpMethod.Post, uri, input, cancellationToken);
    }

    public virtual Task<BaseResponseDataModel<List<WorkflowHistoryModel>>> GetHistoryAsync(string workflow, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<WorkflowHistoryModel>>>(HttpMethod.Get, $"{InstanceRoute(workflow, id)}/history", null, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<WorkflowIncidentModel>>> GetIncidentsAsync(string workflow, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<WorkflowIncidentModel>>>(HttpMethod.Get, $"{InstanceRoute(workflow, id)}/incidents", null, cancellationToken);

    /// <summary>Retries the failed step of a <see cref="WorkflowInstanceStatus.Faulted"/> instance.</summary>
    public virtual Task<BaseResponseDataModel<WorkflowInstanceModel>> RetryAsync(string workflow, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<WorkflowInstanceModel>>(HttpMethod.Post, $"{InstanceRoute(workflow, id)}/retry", null, cancellationToken);

    protected string WorkflowRoute(string workflow) =>
        $"{RoutePrefix.TrimEnd('/')}/{EscapePathSegment(workflow, nameof(workflow))}";

    protected string InstanceRoute(string workflow, Guid id) =>
        $"{WorkflowRoute(workflow)}/instances/{id.ToString("D", CultureInfo.InvariantCulture)}";

    private static void AppendQuery(StringBuilder query, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        query.Append(query.Length == 0 ? '?' : '&')
            .Append(name)
            .Append('=')
            .Append(Uri.EscapeDataString(value));
    }
}
