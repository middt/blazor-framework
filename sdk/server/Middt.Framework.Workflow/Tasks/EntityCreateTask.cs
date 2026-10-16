using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>entity-create</c> (or <c>entity-insert</c>): inserts an entity into the database dynamically.
/// Config: <c>{ "entity": "Store", "values": { ... }, "into": "storeId", "uniqueBy": ["storeName"] }</c>.
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

        SetEntityProperties(instance, entityType, values);
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
