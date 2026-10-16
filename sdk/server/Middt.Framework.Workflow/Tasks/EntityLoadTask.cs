using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>entity-load</c> (or <c>entity-find</c>): reads an entity by primary key into workflow data.
/// Config: <c>{ "entity": "Store", "key": "{{ data.storeId }}", "into": "store" }</c>.
/// </summary>
public sealed class EntityLoadTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["entity"]?.GetValue<string>() is null)
        {
            yield return "entity-load: 'entity' is required.";
        }
        if (task.Config["into"]?.GetValue<string>() is null)
        {
            yield return "entity-load: 'into' is required.";
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = ResolveDbContext(context);
        var entityName = context.RenderText("entity") ?? context.Config["entity"]!.GetValue<string>();
        var entityType = ResolveEntityType(db, entityName);
        var into = context.Config["into"]!.GetValue<string>();

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
            else
            {
                JsonPath.Set(context.Data, into, null);
            }
            return;
        }

        foreach (var nav in entityType.GetNavigations())
        {
            if (!nav.IsCollection)
            {
                try
                {
                    await db.Entry(entity).Reference(nav.Name).LoadAsync(cancellationToken);
                }
                catch
                {
                    // Ignore if cannot load
                }
            }
        }

        context.Data.Remove("error");
        JsonPath.Set(context.Data, into, SerializeEntity(entity, entityType));
    }
}
