using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>Validates all definitions at startup (fail fast) and optionally creates the workflow tables.</summary>
internal sealed class WorkflowStartup(IServiceScopeFactory scopeFactory, WorkflowRegistry registry, IOptions<MiddtWorkflowOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.EnsureLoaded();

        if (!options.Value.AutoCreateSchema)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        if (!await TablesExistAsync(db, cancellationToken))
        {
            // Tables of this context only (the database may contain other application tables).
            if (options.Value.Schema is { Length: > 0 } schema && !db.Database.ProviderName!.Contains("Sqlite", StringComparison.Ordinal))
            {
                await EnsureSchemaAsync(db, schema, cancellationToken);
            }

            await creator.CreateTablesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> TablesExistAsync(WorkflowDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'WorkflowInstances'").SingleAsync(cancellationToken) > 0;
        }

        var schema = options.Value.Schema ?? (db.Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true ? "dbo" : "public");
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {schema} AND table_name = 'WorkflowInstances'").SingleAsync(cancellationToken) > 0;
    }

    private static async Task EnsureSchemaAsync(WorkflowDbContext db, string schema, CancellationToken cancellationToken)
    {
        // Identifiers cannot be parameterised: only plain identifiers are accepted.
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,63}$"))
        {
            throw new InvalidOperationException($"Invalid workflow schema name '{schema}'.");
        }

        var provider = db.Database.ProviderName ?? string.Empty;
#pragma warning disable EF1002 // schema is a validated identifier
        if (provider.Contains("SqlServer", StringComparison.Ordinal))
        {
            await db.Database.ExecuteSqlRawAsync($"IF SCHEMA_ID(N'{schema}') IS NULL EXEC('CREATE SCHEMA [{schema}]')", cancellationToken);
        }
        else if (provider.Contains("Npgsql", StringComparison.Ordinal))
        {
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
        }
#pragma warning restore EF1002
    }
}
