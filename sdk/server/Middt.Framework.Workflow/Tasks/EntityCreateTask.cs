using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>entity-create</c> (or <c>entity-insert</c>): inserts an entity into the database dynamically.
/// Config: <c>{ "entity": "Store", "values": { ... }, "into": "storeId", "uniqueBy": ["storeName"] }</c>.
/// Related collection rows can be created in the same SaveChanges with <c>children</c>.
/// </summary>
public sealed class EntityCreateTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["entity"]?.GetValue<string>() is null)
        {
            yield return "entity-create: 'entity' is required.";
        }

        foreach (var err in expressions.ValidateTemplate(task.Config))
        {
            yield return "entity-create: " + err;
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var into = context.Config["into"]?.GetValue<string>() ?? "id";

        var markerPath = context.Config["idempotencyKey"]?.GetValue<string>() ?? into;
        if (JsonPath.Get(context.Data, markerPath) is JsonValue val && val.GetValueKind() is not JsonValueKind.Null)
        {
            return;
        }

        var db = ResolveDbContext(context);
        var entityName = context.RenderText("entity") ?? context.Config["entity"]!.GetValue<string>();
        var entityType = ResolveEntityType(db, entityName);
        var values = RenderValues(context);

        if (context.Config["uniqueBy"] is { } uniqueByNode)
        {
            IReadOnlyList<string> uniqueProps = uniqueByNode is JsonArray arr
                ? arr.Select(x => x?.GetValue<string>()).OfType<string>().Where(x => !string.IsNullOrEmpty(x)).ToList()
                : [uniqueByNode.GetValue<string>() ?? string.Empty];

            if (await CheckUniqueAsync(db, entityType, values, uniqueProps, context, cancellationToken))
            {
                db.ChangeTracker.Clear();
                return;
            }
        }

        if (await CheckConflictIfAsync(db, context, cancellationToken))
        {
            return;
        }

        context.Data.Remove("error");
        var instance = Activator.CreateInstance(entityType.ClrType)
            ?? throw new InvalidOperationException($"Could not instantiate '{entityType.ClrType.FullName}'.");

        SetEntityProperties(instance, entityType, values, isInsert: true);
        if (context.Config["dateOffsets"] is JsonObject offsets)
        {
            foreach (var (name, offset) in offsets)
            {
                var property = entityType.GetProperties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property?.PropertyInfo is not { CanRead: true, CanWrite: true } info || info.PropertyType != typeof(DateTime))
                {
                    throw new InvalidOperationException($"'{name}' must be a writable DateTime property.");
                }

                var days = context.Expressions.Render(offset, context.Scope)?.GetValue<int>()
                    ?? throw new InvalidOperationException($"dateOffsets.{name} must be a number of days.");
                info.SetValue(instance, ((DateTime)info.GetValue(instance)!).AddDays(days));
            }
        }

        if (context.Config["children"] is JsonObject children)
        {
            foreach (var (name, rows) in children)
            {
                var navigation = entityType.GetNavigations().FirstOrDefault(n => n.IsCollection && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
                var collection = navigation?.PropertyInfo?.GetValue(instance);
                var add = navigation?.PropertyInfo?.PropertyType.GetMethod("Add");
                if (navigation is null || collection is null || add is null || rows is not JsonArray array)
                {
                    throw new InvalidOperationException($"'children.{name}' must name a writable collection navigation and contain an array.");
                }

                foreach (var row in array)
                {
                    var child = Activator.CreateInstance(navigation.TargetEntityType.ClrType)
                        ?? throw new InvalidOperationException($"Could not instantiate child entity '{navigation.TargetEntityType.Name}'.");
                    SetEntityProperties(child, navigation.TargetEntityType,
                        context.Expressions.Render(row, context.Scope) as JsonObject
                        ?? throw new InvalidOperationException($"'children.{name}' rows must be objects."),
                        isInsert: true);
                    add.Invoke(collection, [child]);
                }
            }
        }
        db.Add(instance);
        await db.SaveChangesAsync(cancellationToken);

        var pk = ExtractPrimaryKey(instance, entityType);
        var intoEntity = context.Config["intoEntity"]?.GetValue<bool>() ?? false;
        if (intoEntity)
        {
            JsonPath.Set(context.Data, into, SerializeEntity(instance, entityType));
        }
        else
        {
            JsonPath.Set(context.Data, into, pk);
        }
    }
}
