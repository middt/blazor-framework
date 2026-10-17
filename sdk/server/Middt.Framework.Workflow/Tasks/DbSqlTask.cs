using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>db-sql</c> (or <c>db-execute</c>): executes parameterized raw SQL commands against the database.
/// Config: <c>{ "sql": "UPDATE ... WHERE id = {0}", "parameters": ["{{ data.id }}"], "into": "affected" }</c>.
/// </summary>
public sealed class DbSqlTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["sql"]?.GetValue<string>() is null)
        {
            yield return "db-sql: 'sql' is required.";
        }
        foreach (var err in expressions.ValidateTemplate(task.Config["parameters"]))
        {
            yield return "db-sql: " + err;
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = ResolveDbContext(context);
        var sql = context.Config["sql"]!.GetValue<string>();

        var parametersList = new List<object>();
        if (context.Config["parameters"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var rendered = context.Expressions.Render(item, context.Scope);
                parametersList.Add(rendered switch
                {
                    JsonValue val when val.TryGetValue<long>(out var l) => l,
                    JsonValue val when val.TryGetValue<double>(out var d) => d,
                    JsonValue val when val.TryGetValue<bool>(out var b) => b,
                    JsonValue val when val.TryGetValue<string>(out var s) => s,
                    JsonValue val => val.ToJsonString(),
                    null => DBNull.Value,
                    _ => rendered.ToJsonString()
                });
            }
        }

        var affected = await db.Database.ExecuteSqlRawAsync(sql, parametersList, cancellationToken);
        if (context.Config["into"]?.GetValue<string>() is { Length: > 0 } into)
        {
            JsonPath.Set(context.Data, into, JsonValue.Create(affected));
        }
    }
}
