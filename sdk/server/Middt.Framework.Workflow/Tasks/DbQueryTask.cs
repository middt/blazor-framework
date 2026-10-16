using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Workflow.Tasks;

/// <summary>
/// <c>db-query</c>: executes a SQL query returning either a scalar value or a list of record objects.
/// Config: <c>{ "sql": "SELECT quantity FROM ... WHERE id = @p0", "parameters": ["{{ data.id }}"], "into": "result", "singleValue": true }</c>.
/// </summary>
public sealed class DbQueryTask : EntityTaskBase
{
    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        if (task.Config["sql"]?.GetValue<string>() is null)
        {
            yield return "db-query: 'sql' is required.";
        }
        if (task.Config["into"]?.GetValue<string>() is null)
        {
            yield return "db-query: 'into' is required.";
        }
    }

    public override async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var db = ResolveDbContext(context);
        var rawSql = context.Config["sql"]!.GetValue<string>();
        var sql = context.Expressions.RenderText(rawSql, context.Scope) ?? rawSql;
        var into = context.Config["into"]!.GetValue<string>();
        var isSingleValue = context.Config["singleValue"]?.GetValue<bool>() ?? false;

        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await conn.OpenAsync(cancellationToken);
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            if (context.Config["parameters"] is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    sql = sql.Replace($"{{{i}}}", $"@p{i}", StringComparison.Ordinal);
                    var param = cmd.CreateParameter();
                    param.ParameterName = $"@p{i}";
                    var rendered = context.Expressions.Render(arr[i], context.Scope);
                    param.Value = rendered switch
                    {
                        JsonValue val when val.TryGetValue<long>(out var l) => l,
                        JsonValue val when val.TryGetValue<double>(out var d) => d,
                        JsonValue val when val.TryGetValue<bool>(out var b) => b,
                        JsonValue val when val.TryGetValue<string>(out var s) => s,
                        JsonValue val => val.ToJsonString(),
                        null => DBNull.Value,
                        _ => rendered.ToJsonString()
                    };
                    cmd.Parameters.Add(param);
                }
            }

            cmd.CommandText = sql;

            if (isSingleValue)
            {
                var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                JsonNode? node = scalar switch
                {
                    null or DBNull => null,
                    int iv => JsonValue.Create(iv),
                    long lv => JsonValue.Create(lv),
                    decimal mv => JsonValue.Create(mv),
                    double dv => JsonValue.Create(dv),
                    bool bv => JsonValue.Create(bv),
                    string sv => JsonValue.Create(sv),
                    _ => JsonValue.Create(scalar.ToString())
                };
                JsonPath.Set(context.Data, into, node);
            }
            else
            {
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var results = new JsonArray();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new JsonObject();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var name = reader.GetName(i);
                        if (reader.IsDBNull(i))
                        {
                            row[name] = null;
                        }
                        else
                        {
                            var val = reader.GetValue(i);
                            row[name] = val switch
                            {
                                int iv => JsonValue.Create(iv),
                                long lv => JsonValue.Create(lv),
                                decimal mv => JsonValue.Create(mv),
                                double dv => JsonValue.Create(dv),
                                bool bv => JsonValue.Create(bv),
                                string sv => JsonValue.Create(sv),
                                DateTime dtv => JsonValue.Create(dtv.ToString("O", CultureInfo.InvariantCulture)),
                                Guid gv => JsonValue.Create(gv.ToString()),
                                _ => JsonValue.Create(val.ToString())
                            };
                        }
                    }
                    results.Add(row);
                }
                JsonPath.Set(context.Data, into, results);
            }
        }
        finally
        {
            if (wasClosed)
            {
                await conn.CloseAsync();
            }
        }
    }
}
