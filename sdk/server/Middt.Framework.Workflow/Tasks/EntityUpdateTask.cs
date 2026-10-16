using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>entity-update</c>: updates an existing entity record by primary key.
/// Config: <c>{ "entity": "Store", "key": "{{ data.storeId }}", "values": { "phone": "..." } }</c>.
/// </summary>
public sealed class EntityUpdateTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["entity"]?.GetValue<string>() is null)
        {
            yield return "entity-update: 'entity' is required.";
        }
        if (task.Config["key"] is null && task.Config["id"] is null)
        {
            yield return "entity-update: 'key' (or 'id') is required.";
        }
        foreach (var err in expressions.ValidateTemplate(task.Config))
        {
            yield return "entity-update: " + err;
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = ResolveDbContext(context);
        var entityName = context.RenderText("entity") ?? context.Config["entity"]!.GetValue<string>();
        var entityType = ResolveEntityType(db, entityName);
        var values = RenderValues(context);

        var keyNode = context.Render("key") ?? context.Render("id");
        var pk = entityType.FindPrimaryKey();
        if (pk is null || pk.Properties.Count == 0)
        {
            throw new InvalidOperationException($"Entity '{entityType.Name}' has no primary key defined.");
        }

        object?[] keyValues;
        if (pk.Properties.Count == 1)
        {
            var singleVal = ConvertToPropertyType(keyNode, pk.Properties[0].ClrType);
            keyValues = [singleVal];
        }
        else if (keyNode is JsonObject keyObj)
        {
            keyValues = pk.Properties.Select(p => ConvertToPropertyType(keyObj[p.Name], p.ClrType)).ToArray();
        }
        else
        {
            throw new InvalidOperationException($"Entity '{entityType.Name}' has a composite primary key; provide a key object.");
        }

        var entity = await db.FindAsync(entityType.ClrType, keyValues, cancellationToken);
        if (entity is null)
        {
            var isRequired = context.Config["required"]?.GetValue<bool>() ?? true;
            if (isRequired)
            {
                var customError = context.Config["error"]?.GetValue<string>();
                context.Data["error"] = (customError is not null ? context.Expressions.RenderText(customError, context.Scope) : null) ?? "Kayıt bulunamadı.";
            }
            return;
        }

        if (await CheckConflictIfAsync(db, context, cancellationToken))
        {
            return;
        }

        if (context.Config["uniqueBy"] is { } uniqueByNode)
        {
            IReadOnlyList<string> uniqueProps = uniqueByNode is JsonArray arr
                ? arr.Select(x => x?.GetValue<string>()).OfType<string>().Where(x => !string.IsNullOrEmpty(x)).ToList()
                : [uniqueByNode.GetValue<string>() ?? string.Empty];

            if (await CheckUniqueAsync(db, entityType, values, uniqueProps, context, cancellationToken, keyValues))
            {
                db.ChangeTracker.Clear();
                return;
            }
        }

        context.Data.Remove("error");
        SetEntityProperties(entity, entityType, values);
        await db.SaveChangesAsync(cancellationToken);

        if (context.Config["into"]?.GetValue<string>() is { Length: > 0 } into)
        {
            JsonPath.Set(context.Data, into, SerializeEntity(entity, entityType));
        }
    }
}
