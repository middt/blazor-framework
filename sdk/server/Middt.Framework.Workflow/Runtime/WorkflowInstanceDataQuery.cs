using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Schema;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Queries and projects <see cref="WorkflowInstance"/> JSON documents (<c>Data</c> + instance metadata) into flat objects
/// suitable for <c>api/v1/{entity}/getall|getitems|getbyid</c>, Server-Driven UI grids/KPIs, and <c>x-lookup</c> selects
/// without requiring relational domain tables.
/// </summary>
public sealed class WorkflowInstanceDataQuery(WorkflowDbContext db, WorkflowRegistry registry)
{
    private static readonly Dictionary<string, string> EntityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CustomerOnboarding"] = "customer-onboarding",
        ["Customer"] = "customer-onboarding",
        ["CreditAllocation"] = "credit-allocation",
        ["Credit"] = "credit-allocation",
        ["OrderPaymentOrchestration"] = "order-payment-orchestration",
        ["OrderPayment"] = "order-payment-orchestration",
        ["Order"] = "order-payment-orchestration",
        ["SlaEscalation"] = "sla-escalation",
        ["Sla"] = "sla-escalation",
        ["RiskLimitEval"] = "risk-limit-eval",
        ["RiskLimit"] = "risk-limit-eval",
    };

    /// <summary>Returns all accessible instances of <paramref name="entity"/> with <c>dataSchema</c> role/mask projection applied.</summary>
    public async Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(
        string entity,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var items = await LoadProjectedInstancesAsync(entity, user, cancellationToken);
        return new BaseResponseDataModel<List<JsonObject>>
        {
            Result = ResultEnum.Success,
            Count = items.Count,
            Data = items,
        };
    }

    /// <summary>Returns filtered, sorted, and paged accessible instances of <paramref name="entity"/>.</summary>
    public async Task<BaseResponseDataModel<List<JsonObject>>> GetItemsAsync(
        string entity,
        JsonObject? searchRequest,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var items = await LoadProjectedInstancesAsync(entity, user, cancellationToken);

        if (searchRequest?["requestModel"] is JsonObject filter)
        {
            items = [.. items.Where(item => MatchesFilter(item, filter))];
        }

        var sortProperty = searchRequest?["sortProperty"]?.GetValue<string>();
        var ascending = searchRequest?["sortReverse"]?.GetValue<bool>() ?? false;
        if (!string.IsNullOrWhiteSpace(sortProperty))
        {
            items = ascending
                ? [.. items.OrderBy(i => SortKey(i, sortProperty), StringComparer.OrdinalIgnoreCase)]
                : [.. items.OrderByDescending(i => SortKey(i, sortProperty), StringComparer.OrdinalIgnoreCase)];
        }

        var page = Math.Max(1, searchRequest?["currentPage"]?.GetValue<int>() ?? 1);
        var pageSize = Math.Clamp(searchRequest?["requestItemSize"]?.GetValue<int>() ?? 20, 1, 100);
        var total = items.Count;
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new BaseResponseDataModel<List<JsonObject>>
        {
            Result = ResultEnum.Success,
            Count = total,
            Data = paged,
        };
    }

    /// <summary>Returns a single accessible instance of <paramref name="entity"/> by <c>Id</c> or <c>BusinessKey</c>.</summary>
    public async Task<BaseResponseDataModel<JsonObject>> GetByIdAsync(
        string entity,
        string id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var items = await LoadProjectedInstancesAsync(entity, user, cancellationToken);
        var match = items.FirstOrDefault(i =>
            string.Equals(i["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i["key"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));

        return new BaseResponseDataModel<JsonObject>
        {
            Result = match is not null ? ResultEnum.Success : ResultEnum.Error,
            Count = match is not null ? 1 : 0,
            Data = match,
        };
    }

    private async Task<List<JsonObject>> LoadProjectedInstancesAsync(string entity, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var workflowKey = ResolveWorkflowKey(entity);
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking();
        if (workflowKey is not null)
        {
            query = query.Where(i => i.WorkflowKey == workflowKey);
        }

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var result = new List<JsonObject>(rows.Count);
        foreach (var row in rows)
        {
            var definition = TryGetDefinition(row.WorkflowKey, row.WorkflowVersion);
            if (definition is not null && !CanReadInstance(definition, row, user))
            {
                continue;
            }

            result.Add(FlattenInstance(row, definition, user));
        }

        return result;
    }

    private WorkflowDefinition? TryGetDefinition(string key, int version)
    {
        try
        {
            return registry.Get(key, version);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static bool CanReadInstance(WorkflowDefinition definition, WorkflowInstance instance, ClaimsPrincipal user)
    {
        if (definition.Roles.Count > 0 && !definition.Roles.Any(user.IsInRole))
        {
            return false;
        }

        var access = definition.InstanceAccess;
        if (access is null || !access.IsOwnerOnly)
        {
            return true;
        }

        if (access.SupervisorRoles.Any(user.IsInRole))
        {
            return true;
        }

        var userName = user.Identity?.Name;
        return !string.IsNullOrEmpty(userName) && string.Equals(instance.StartedBy, userName, StringComparison.Ordinal);
    }

    private static JsonObject FlattenInstance(WorkflowInstance instance, WorkflowDefinition? definition, ClaimsPrincipal user)
    {
        var rawData = JsonNode.Parse(string.IsNullOrWhiteSpace(instance.Data) ? "{}" : instance.Data) as JsonObject ?? [];
        var projected = JsonSchemaValidator.ProjectData(rawData, definition?.DataSchema, ExpressionUser.FromPrincipal(user));

        var flat = new JsonObject();
        foreach (var (propName, propValue) in projected)
        {
            flat[propName] = propValue?.DeepClone();
        }

        var id = instance.Id.ToString("D");
        flat["id"] = id;
        flat["key"] = instance.BusinessKey ?? id;
        flat["businessKey"] = instance.BusinessKey;
        flat["workflow"] = instance.WorkflowKey;
        flat["version"] = instance.WorkflowVersion;
        flat["state"] = instance.State;
        flat["stateTitle"] = definition?.FindState(instance.State)?.Title ?? instance.State;
        flat["status"] = instance.Status.ToString();
        flat["startedBy"] = instance.StartedBy;
        flat["lastExecutedBy"] = instance.LastExecutedBy;
        flat["createdAt"] = new DateTimeOffset(DateTime.SpecifyKind(instance.CreatedAt, DateTimeKind.Utc));
        flat["updatedAt"] = new DateTimeOffset(DateTime.SpecifyKind(instance.UpdatedAt, DateTimeKind.Utc));
        return flat;
    }

    private static bool MatchesFilter(JsonObject item, JsonObject filter)
    {
        foreach (var (key, expected) in filter)
        {
            if (expected is null)
            {
                continue;
            }

            var expectedText = expected.ToString().Trim();
            if (string.IsNullOrEmpty(expectedText))
            {
                continue;
            }

            var actualText = item[key]?.ToString() ?? string.Empty;
            if (!actualText.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string SortKey(JsonObject item, string property) =>
        item[property] switch
        {
            JsonValue v when v.TryGetValue<decimal>(out var d) => d.ToString("000000000000000.0000", CultureInfo.InvariantCulture),
            JsonNode n => n.ToString(),
            null => string.Empty,
        };

    private static string? ResolveWorkflowKey(string entity)
    {
        if (string.Equals(entity, "WorkflowInstance", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (EntityAliases.TryGetValue(entity, out var mapped))
        {
            return mapped;
        }

        var sb = new StringBuilder(entity.Length + 4);
        for (var i = 0; i < entity.Length; i++)
        {
            var c = entity[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(entity[i - 1]))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
