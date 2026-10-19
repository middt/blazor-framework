using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
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
    /// <summary>Returns accessible instances of <paramref name="entity"/> (clamped to 200 for unpaged lookups/KPIs) with <c>dataSchema</c> role/mask projection applied.</summary>
    public async Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(
        string entity,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var query = BuildAccessibleQuery(entity, user);
        if (query is null)
        {
            return new BaseResponseDataModel<List<JsonObject>> { Result = ResultEnum.Success, Count = 0, Data = [] };
        }

        var total = await query.IgnoreAutoIncludes().CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var items = ProjectRows(rows, user);
        return new BaseResponseDataModel<List<JsonObject>>
        {
            Result = ResultEnum.Success,
            Count = total,
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
        var query = BuildAccessibleQuery(entity, user);
        if (query is null)
        {
            return new BaseResponseDataModel<List<JsonObject>> { Result = ResultEnum.Success, Count = 0, Data = [] };
        }

        var page = Math.Max(1, searchRequest?["currentPage"]?.GetValue<int>() ?? 1);
        var pageSize = Math.Clamp(searchRequest?["requestItemSize"]?.GetValue<int>() ?? 20, 1, 100);
        var filter = searchRequest?["requestModel"] as JsonObject;
        var hasActiveFilter = HasNonEmptyFilter(filter);
        var sortProperty = searchRequest?["sortProperty"]?.GetValue<string>();
        var ascending = searchRequest?["sortReverse"]?.GetValue<bool>() ?? false;

        if (!hasActiveFilter && (string.IsNullOrWhiteSpace(sortProperty) || string.Equals(sortProperty, "createdAt", StringComparison.OrdinalIgnoreCase)))
        {
            var total = await query.IgnoreAutoIncludes().CountAsync(cancellationToken);
            var ordered = ascending ? query.OrderBy(i => i.CreatedAt) : query.OrderByDescending(i => i.CreatedAt);
            var rows = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new BaseResponseDataModel<List<JsonObject>>
            {
                Result = ResultEnum.Success,
                Count = total,
                Data = ProjectRows(rows, user),
            };
        }

        if (filter is not null)
        {
            foreach (var (k, v) in filter)
            {
                var text = v?.ToString().Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (string.Equals(k, "key", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "businessKey", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(i => i.BusinessKey != null && i.BusinessKey.Contains(text));
                }
                else if (string.Equals(k, "state", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(i => i.State.Contains(text));
                }
                else
                {
                    query = query.Where(i => i.InstanceData != null && i.InstanceData.Data.Contains(text));
                }
            }
        }

        var candidateRows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var items = ProjectRows(candidateRows, user);
        if (filter is not null)
        {
            items = [.. items.Where(item => MatchesFilter(item, filter))];
        }

        if (!string.IsNullOrWhiteSpace(sortProperty))
        {
            items = ascending
                ? [.. items.OrderBy(i => SortKey(i, sortProperty), StringComparer.OrdinalIgnoreCase)]
                : [.. items.OrderByDescending(i => SortKey(i, sortProperty), StringComparer.OrdinalIgnoreCase)];
        }

        var filteredTotal = items.Count;
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new BaseResponseDataModel<List<JsonObject>>
        {
            Result = ResultEnum.Success,
            Count = filteredTotal,
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
        var query = BuildAccessibleQuery(entity, user);
        if (query is null)
        {
            return new BaseResponseDataModel<JsonObject> { Result = ResultEnum.Error, Count = 0, Data = null };
        }

        if (Guid.TryParse(id, out var guid))
        {
            query = query.Where(i => i.Id == guid || i.BusinessKey == id);
        }
        else
        {
            query = query.Where(i => i.BusinessKey == id);
        }

        var row = await query.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return new BaseResponseDataModel<JsonObject> { Result = ResultEnum.Error, Count = 0, Data = null };
        }

        var definition = TryGetDefinition(row.WorkflowKey, row.WorkflowVersion);
        var match = FlattenInstance(row, definition, user);

        return new BaseResponseDataModel<JsonObject>
        {
            Result = ResultEnum.Success,
            Count = 1,
            Data = match,
        };
    }

    private IQueryable<WorkflowInstance>? BuildAccessibleQuery(string entity, ClaimsPrincipal principal)
    {
        var workflowKey = ResolveWorkflowKey(entity);
        var wfUser = WorkflowUser.From(principal);
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking();

        if (workflowKey is not null)
        {
            var versions = TryGetVersions(workflowKey);
            if (versions.Count > 0)
            {
                var readable = versions
                    .Where(d => d.Roles.Count == 0 || d.Roles.Any(wfUser.IsInRole))
                    .ToArray();
                if (readable.Length == 0)
                {
                    return null;
                }

                var allVersions = readable.Where(d => CanSeeAllInstances(d, wfUser)).Select(d => d.Version).ToArray();
                var ownerVersions = readable.Where(d => !CanSeeAllInstances(d, wfUser)).Select(d => d.Version).ToArray();
                var owner = wfUser.AuditName;

                if (ownerVersions.Length == 0 && allVersions.Length == versions.Count)
                {
                    return query.Where(i => i.WorkflowKey == workflowKey);
                }

                if (allVersions.Length == 0 && ownerVersions.Length == versions.Count)
                {
                    return query.Where(i => i.WorkflowKey == workflowKey && i.StartedBy == owner);
                }

                return query.Where(i => i.WorkflowKey == workflowKey
                    && (allVersions.Contains(i.WorkflowVersion) || (ownerVersions.Contains(i.WorkflowVersion) && i.StartedBy == owner)));
            }

            return query.Where(i => i.WorkflowKey == workflowKey);
        }

        return query;
    }

    private List<JsonObject> ProjectRows(List<WorkflowInstance> rows, ClaimsPrincipal user)
    {
        var wfUser = WorkflowUser.From(user);
        var result = new List<JsonObject>(rows.Count);
        foreach (var row in rows)
        {
            var definition = TryGetDefinition(row.WorkflowKey, row.WorkflowVersion);
            if (definition is not null && !CanReadInstance(definition, row, wfUser))
            {
                continue;
            }

            result.Add(FlattenInstance(row, definition, user));
        }

        return result;
    }

    private static bool HasNonEmptyFilter(JsonObject? filter)
    {
        if (filter is null)
        {
            return false;
        }

        foreach (var (_, v) in filter)
        {
            if (!string.IsNullOrWhiteSpace(v?.ToString()))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<WorkflowDefinition> TryGetVersions(string key)
    {
        try
        {
            return registry.GetVersions(key);
        }
        catch (WorkflowNotFoundException)
        {
            return [];
        }
    }

    private WorkflowDefinition? TryGetDefinition(string key, int version)
    {
        try
        {
            return registry.Get(key, version);
        }
        catch (WorkflowNotFoundException)
        {
            return null;
        }
    }

    private static bool CanSeeAllInstances(WorkflowDefinition definition, WorkflowUser user) =>
        user.IsSystem
        || definition.InstanceAccess is not { IsOwnerOnly: true } access
        || access.SupervisorRoles.Any(user.IsInRole);

    private static bool CanReadInstance(WorkflowDefinition definition, WorkflowInstance instance, WorkflowUser user)
    {
        if (definition.Roles.Count > 0 && !definition.Roles.Any(user.IsInRole))
        {
            return false;
        }

        return CanSeeAllInstances(definition, user)
            || (!string.IsNullOrEmpty(instance.StartedBy) && string.Equals(instance.StartedBy, user.AuditName, StringComparison.Ordinal));
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

    private string? ResolveWorkflowKey(string entity)
    {
        if (string.IsNullOrWhiteSpace(entity) || string.Equals(entity, "WorkflowInstance", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (TryGetVersions(entity) is { Count: > 0 } direct)
        {
            return direct[0].Key;
        }

        var kebab = ToKebabCase(entity);
        if (TryGetVersions(kebab) is { Count: > 0 } kebabMatch)
        {
            return kebabMatch[0].Key;
        }

        var normalized = NormalizeKey(entity);
        foreach (var def in registry.Latest)
        {
            if (string.Equals(NormalizeKey(def.Key), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return def.Key;
            }
        }

        var prefixMatches = registry.Latest
            .Where(d => d.Key.StartsWith(kebab + "-", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (prefixMatches.Count == 1)
        {
            return prefixMatches[0];
        }

        return kebab;
    }

    private static string ToKebabCase(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '_' or ' ')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }

                continue;
            }

            if (char.IsUpper(c) && i > 0 && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1])))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string NormalizeKey(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
