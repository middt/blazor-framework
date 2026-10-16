using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>entity-delete</c>: removes an entity record by primary key.
/// Config: <c>{ "entity": "Store", "key": "{{ data.storeId }}" }</c>.
/// </summary>
public sealed class EntityDeleteTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["entity"]?.GetValue<string>() is null)
        {
            yield return "entity-delete: 'entity' is required.";
        }
        if (task.Config["key"] is null && task.Config["id"] is null)
        {
            yield return "entity-delete: 'key' (or 'id') is required.";
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = ResolveDbContext(context);
        var entityName = context.RenderText("entity") ?? context.Config["entity"]!.GetValue<string>();
        var entityType = ResolveEntityType(db, entityName);

        var keyNode = context.Render("key") ?? context.Render("id");
        var pk = entityType.FindPrimaryKey();
        if (pk is null || pk.Properties.Count == 0)
        {
            throw new InvalidOperationException($"Entity '{entityType.Name}' has no primary key defined.");
        }

        object?[] keyValues = [ConvertToPropertyType(keyNode, pk.Properties[0].ClrType)];
        var entity = await db.FindAsync(entityType.ClrType, keyValues, cancellationToken);
        if (entity is not null)
        {
            db.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
