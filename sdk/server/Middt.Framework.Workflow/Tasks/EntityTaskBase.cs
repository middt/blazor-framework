using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// Base class for generic database and entity workflow tasks (<c>entity-create</c>, <c>entity-update</c>, etc.).
/// Resolves the application's <see cref="DbContext"/> and provides reflection/metadata helpers so business logic
/// can execute directly against tables without writing custom C# task classes or redeploying the application.
/// </summary>
public abstract class EntityTaskBase : IWorkflowTask
{
    private static readonly HashSet<string> ReservedConfigKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "entity", "into", "key", "id", "values", "uniqueBy", "idempotencyKey", "context", "required", "intoEntity", "singleValue", "parameters", "sql", "conflictIf"
    };

    public virtual IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) => [];

    public abstract Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken);

    protected static DbContext ResolveDbContext(WorkflowTaskContext context)
    {
        var options = context.Services.GetRequiredService<IOptions<MiddtWorkflowOptions>>().Value;

        if (context.Config["context"]?.GetValue<string>() is { Length: > 0 } contextName)
        {
            var match = options.ApplicationDbContextTypes.FirstOrDefault(t =>
                string.Equals(t.Name, contextName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return (DbContext)context.Services.GetRequiredService(match);
            }
        }

        if (options.ApplicationDbContextTypes.Count > 0)
        {
            return (DbContext)context.Services.GetRequiredService(options.ApplicationDbContextTypes[0]);
        }

        if (context.Services.GetService<DbContext>() is { } db && db.GetType().Name != "WorkflowDbContext")
        {
            return db;
        }

        throw new InvalidOperationException("No application DbContext is configured for entity workflow tasks. Configure it with options.UseApplicationDbContext<TDbContext>().");
    }

    protected static IEntityType ResolveEntityType(DbContext db, string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var entityType = db.Model.GetEntityTypes().FirstOrDefault(t =>
            string.Equals(t.ClrType.Name, entityName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.GetTableName(), entityName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, entityName, StringComparison.OrdinalIgnoreCase));

        return entityType ?? throw new InvalidOperationException($"Entity '{entityName}' was not found in DbContext '{db.GetType().Name}'.");
    }

    protected static JsonObject RenderValues(WorkflowTaskContext context)
    {
        if (context.Config["values"] is JsonObject valuesObj)
        {
            return context.Expressions.Render(valuesObj, context.Scope) as JsonObject ?? [];
        }

        var rendered = new JsonObject();
        foreach (var (k, v) in context.Config)
        {
            if (!ReservedConfigKeys.Contains(k) && v is not null)
            {
                rendered[k] = context.Expressions.Render(v, context.Scope);
            }
        }

        return rendered;
    }

    protected static void SetEntityProperties(object instance, IEntityType entityType, JsonObject values)
    {
        foreach (var (propertyName, node) in values)
        {
            var prop = entityType.FindProperty(propertyName)
                ?? entityType.GetProperties().FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

            if (prop?.PropertyInfo is { CanWrite: true })
            {
                var converted = ConvertToPropertyType(node, prop.ClrType);
                prop.PropertyInfo.SetValue(instance, converted);
            }
        }
    }

    protected static object? ConvertToPropertyType(JsonNode? node, Type targetType)
    {
        if (node is null)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (node is JsonValue val)
        {
            if (val.GetValueKind() == JsonValueKind.Null)
            {
                return null;
            }

            if (underlying == typeof(string))
            {
                return val.TryGetValue<string>(out var s) ? s : val.ToJsonString();
            }

            var rawText = val.TryGetValue<string>(out var text) ? text.Trim() : val.ToJsonString();

            if (underlying == typeof(int))
                return int.Parse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture);

            if (underlying == typeof(long))
                return long.Parse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture);

            if (underlying == typeof(short))
                return short.Parse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture);

            if (underlying == typeof(byte))
            {
                if (val.TryGetValue<bool>(out var bVal))
                {
                    return (byte)(bVal ? 1 : 0);
                }
                if (bool.TryParse(rawText, out var b))
                {
                    return (byte)(b ? 1 : 0);
                }
                return byte.Parse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlying == typeof(decimal))
                return decimal.Parse(rawText, NumberStyles.Number, CultureInfo.InvariantCulture);

            if (underlying == typeof(double))
                return double.Parse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (underlying == typeof(float))
                return float.Parse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (underlying == typeof(bool))
            {
                if (val.TryGetValue<bool>(out var bVal))
                {
                    return bVal;
                }
                if (rawText == "1") return true;
                if (rawText == "0") return false;
                return bool.Parse(rawText);
            }

            if (underlying == typeof(Guid))
                return Guid.Parse(rawText);

            if (underlying == typeof(DateTime))
                return DateTime.Parse(rawText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            if (underlying == typeof(DateTimeOffset))
                return DateTimeOffset.Parse(rawText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            if (underlying == typeof(DateOnly))
                return DateOnly.Parse(rawText, CultureInfo.InvariantCulture);

            if (underlying == typeof(TimeOnly))
                return TimeOnly.Parse(rawText, CultureInfo.InvariantCulture);

            if (underlying.IsEnum)
                return Enum.Parse(underlying, rawText, ignoreCase: true);
        }

        return JsonSerializer.Deserialize(node.ToJsonString(), targetType, (JsonSerializerOptions?)null);
    }

    protected static JsonNode? ExtractPrimaryKey(object instance, IEntityType entityType)
    {
        var pk = entityType.FindPrimaryKey();
        if (pk is null || pk.Properties.Count == 0)
        {
            return null;
        }

        if (pk.Properties.Count == 1)
        {
            var val = pk.Properties[0].PropertyInfo?.GetValue(instance);
            return ToJsonValue(val);
        }

        var obj = new JsonObject();
        foreach (var p in pk.Properties)
        {
            obj[p.Name] = ToJsonValue(p.PropertyInfo?.GetValue(instance));
        }

        return obj;
    }

    protected static JsonObject SerializeEntity(object instance, IEntityType entityType)
    {
        var obj = new JsonObject();
        foreach (var prop in entityType.GetProperties())
        {
            if (prop.PropertyInfo is { CanRead: true })
            {
                var val = prop.PropertyInfo.GetValue(instance);
                var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
                var jsonVal = string.Equals(prop.Name, "Active", StringComparison.OrdinalIgnoreCase) && val is byte byteVal
                    ? JsonValue.Create(byteVal == 1)
                    : ToJsonValue(val);

                obj[camelName] = jsonVal;
                if (!string.Equals(camelName, prop.Name, StringComparison.Ordinal))
                {
                    obj[prop.Name] = jsonVal?.DeepClone();
                }
            }
        }

        foreach (var nav in entityType.GetNavigations())
        {
            if (!nav.IsCollection && nav.PropertyInfo is { CanRead: true })
            {
                var related = nav.PropertyInfo.GetValue(instance);
                if (related is not null)
                {
                    var targetEntityType = nav.TargetEntityType;
                    var nameProp = targetEntityType.FindProperty("Name")
                        ?? targetEntityType.FindProperty("Title")
                        ?? targetEntityType.GetProperties().FirstOrDefault(p => p.Name.EndsWith("Name", StringComparison.OrdinalIgnoreCase));
                    if (nameProp?.PropertyInfo is { CanRead: true })
                    {
                        var nameVal = nameProp.PropertyInfo.GetValue(related);
                        if (nameVal is not null)
                        {
                            var targetPropName = JsonNamingPolicy.CamelCase.ConvertName(nameProp.Name);
                            if (string.Equals(nameProp.Name, "Name", StringComparison.OrdinalIgnoreCase))
                            {
                                targetPropName = JsonNamingPolicy.CamelCase.ConvertName(nav.Name + "Name");
                            }

                            if (!obj.ContainsKey(targetPropName))
                            {
                                obj[targetPropName] = ToJsonValue(nameVal);
                            }
                            if (!obj.ContainsKey(nameProp.Name))
                            {
                                obj[nameProp.Name] = ToJsonValue(nameVal);
                            }
                        }
                    }
                    else
                    {
                        var clrProp = targetEntityType.ClrType.GetProperty("Name") ?? targetEntityType.ClrType.GetProperty("Title");
                        if (clrProp is { CanRead: true })
                        {
                            var nameVal = clrProp.GetValue(related);
                            if (nameVal is not null)
                            {
                                var targetPropName = JsonNamingPolicy.CamelCase.ConvertName(nav.Name + "Name");
                                if (!obj.ContainsKey(targetPropName))
                                {
                                    obj[targetPropName] = ToJsonValue(nameVal);
                                }
                            }
                        }
                    }
                }
            }
        }

        return obj;
    }

    protected static async Task<object?> FindConflictAsync(
        DbContext db,
        IEntityType entityType,
        JsonObject filterValues,
        CancellationToken cancellationToken)
    {
        var param = Expression.Parameter(entityType.ClrType, "e");
        Expression? predicate = null;

        foreach (var (name, node) in filterValues)
        {
            if (node is null)
            {
                continue;
            }

            var prop = entityType.FindProperty(name)
                ?? entityType.GetProperties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop?.PropertyInfo is null)
            {
                continue;
            }

            var converted = ConvertToPropertyType(node, prop.ClrType);
            var propAccess = Expression.Property(param, prop.PropertyInfo);
            var constVal = Expression.Constant(converted, prop.ClrType);
            var eq = Expression.Equal(propAccess, constVal);
            predicate = predicate is null ? eq : Expression.AndAlso(predicate, eq);
        }

        if (predicate is null)
        {
            return null;
        }

        var lambda = Expression.Lambda(predicate, param);
        var setMethod = typeof(DbContext).GetMethods()
            .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(entityType.ClrType);
        var queryable = (IQueryable)setMethod.Invoke(db, null)!;

        var firstOrDefaultAsyncMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.FirstOrDefaultAsync) && m.GetParameters().Length == 3)
            .MakeGenericMethod(entityType.ClrType);

        var task = (Task)firstOrDefaultAsyncMethod.Invoke(null, [queryable, lambda, cancellationToken])!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    protected static async Task<bool> CheckConflictIfAsync(
        DbContext db,
        WorkflowTaskContext context,
        CancellationToken cancellationToken)
    {
        if (context.Config["conflictIf"] is not JsonObject conflictObj)
        {
            return false;
        }

        var rawConflictEntity = conflictObj["entity"]?.GetValue<string>();
        var conflictEntityName = (rawConflictEntity is not null ? context.Expressions.RenderText(rawConflictEntity, context.Scope) : null) ?? rawConflictEntity;
        if (string.IsNullOrWhiteSpace(conflictEntityName))
        {
            return false;
        }

        var conflictEntityType = ResolveEntityType(db, conflictEntityName);
        var matchObj = conflictObj["match"] as JsonObject ?? conflictObj["filter"] as JsonObject ?? [];
        var renderedMatch = context.Expressions.Render(matchObj, context.Scope) as JsonObject ?? [];
        var conflictRecord = await FindConflictAsync(db, conflictEntityType, renderedMatch, cancellationToken);
        if (conflictRecord is not null)
        {
            var rawError = conflictObj["error"]?.GetValue<string>() ?? "İlişkili kayıtlar nedeniyle bu işlem yapılamaz.";
            var renderedError = context.Expressions.RenderText(rawError, context.Scope) ?? rawError;

            if (renderedError.Contains("{0}", StringComparison.Ordinal))
            {
                var displayProp = conflictEntityType.GetProperties()
                    .FirstOrDefault(p => p.ClrType == typeof(string) && (p.Name.EndsWith("Name", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Title", StringComparison.OrdinalIgnoreCase)));
                var displayVal = displayProp?.PropertyInfo?.GetValue(conflictRecord)?.ToString() ?? string.Empty;
                renderedError = string.Format(CultureInfo.InvariantCulture, renderedError, displayVal);
            }

            context.Data["error"] = renderedError;
            return true;
        }

        return false;
    }

    protected static async Task<bool> CheckUniqueAsync(
        DbContext db,
        IEntityType entityType,
        JsonObject values,
        IReadOnlyList<string> uniqueProperties,
        WorkflowTaskContext context,
        CancellationToken cancellationToken,
        object?[]? excludeKeyValues = null)
    {
        var param = Expression.Parameter(entityType.ClrType, "e");
        Expression? predicate = null;
        string? firstDuplicateValue = null;

        foreach (var name in uniqueProperties)
        {
            var prop = entityType.FindProperty(name)
                ?? entityType.GetProperties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop?.PropertyInfo is null)
            {
                continue;
            }

            var node = values[name] ?? values[prop.Name];
            if (node is null)
            {
                continue;
            }

            var converted = ConvertToPropertyType(node, prop.ClrType);
            firstDuplicateValue ??= converted?.ToString();
            var propAccess = Expression.Property(param, prop.PropertyInfo);
            var constVal = Expression.Constant(converted, prop.ClrType);
            var eq = Expression.Equal(propAccess, constVal);
            predicate = predicate is null ? eq : Expression.AndAlso(predicate, eq);
        }

        if (predicate is null)
        {
            return false;
        }

        var pk = entityType.FindPrimaryKey();
        if (excludeKeyValues is not null && pk is not null && pk.Properties.Count == excludeKeyValues.Length)
        {
            Expression? pkNotEqual = null;
            for (var i = 0; i < pk.Properties.Count; i++)
            {
                var pkProp = pk.Properties[i];
                if (pkProp.PropertyInfo is not null)
                {
                    var propAccess = Expression.Property(param, pkProp.PropertyInfo);
                    var constVal = Expression.Constant(excludeKeyValues[i], pkProp.ClrType);
                    var notEq = Expression.NotEqual(propAccess, constVal);
                    pkNotEqual = pkNotEqual is null ? notEq : Expression.OrElse(pkNotEqual, notEq);
                }
            }

            if (pkNotEqual is not null)
            {
                predicate = Expression.AndAlso(predicate, pkNotEqual);
            }
        }

        var lambda = Expression.Lambda(predicate, param);
        var setMethod = typeof(DbContext).GetMethods()
            .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(entityType.ClrType);
        var queryable = (IQueryable)setMethod.Invoke(db, null)!;

        var anyAsyncMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.AnyAsync) && m.GetParameters().Length == 3)
            .MakeGenericMethod(entityType.ClrType);

        var task = (Task<bool>)anyAsyncMethod.Invoke(null, [queryable, lambda, cancellationToken])!;
        var exists = await task;

        if (exists)
        {
            var customError = context.Config["uniqueError"]?.GetValue<string>() ?? context.Config["error"]?.GetValue<string>();
            if (customError is not null)
            {
                var rendered = context.Expressions.RenderText(customError, context.Scope) ?? customError;
                if (rendered.Contains("{0}", StringComparison.Ordinal))
                {
                    rendered = string.Format(CultureInfo.InvariantCulture, rendered, firstDuplicateValue);
                }
                context.Data["error"] = rendered;
            }
            else
            {
                context.Data["error"] = $"\"{firstDuplicateValue}\" bilgisine sahip {entityType.ClrType.Name} zaten mevcut.";
            }
            return true;
        }

        return false;
    }

    protected static JsonNode? ToJsonValue(object? val) => val switch
    {
        null => null,
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        short s => JsonValue.Create(s),
        byte b => JsonValue.Create(b),
        decimal m => JsonValue.Create(m),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        Guid g => JsonValue.Create(g.ToString()),
        DateTime dt => JsonValue.Create(dt.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("O", CultureInfo.InvariantCulture)),
        DateOnly d => JsonValue.Create(d.ToString("O", CultureInfo.InvariantCulture)),
        TimeOnly t => JsonValue.Create(t.ToString("O", CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(val.ToString())
    };
}
