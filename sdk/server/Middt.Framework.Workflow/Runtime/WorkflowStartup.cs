using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
        else
        {
            // CreateTables only runs for a new database: add nullable columns and indexes introduced by later framework versions.
            await EnsureColumnsAsync(db, cancellationToken);
            await EnsureIndexesAsync(db, cancellationToken);
        }
    }

    /// <summary>Adds every nullable column of the model that does not exist yet (idempotent; PostgreSQL).</summary>
    internal static async Task EnsureColumnsAsync(WorkflowDbContext db, CancellationToken cancellationToken)
    {
        foreach (var statement in EnsureColumnStatements(db))
        {
#pragma warning disable EF1002 // identifiers and column types come from the EF model, not from user input
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
#pragma warning restore EF1002
        }
    }

    internal static IEnumerable<string> EnsureColumnStatements(WorkflowDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.Ordinal))
        {
            yield break;
        }

        var sql = db.GetService<ISqlGenerationHelper>();
        foreach (var entityType in db.GetService<IDesignTimeModel>().Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            var schema = entityType.GetSchema();
            var tableIdentifier = StoreObjectIdentifier.Table(table, schema);
            var qualifiedTable = sql.DelimitIdentifier(table, schema);
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsPrimaryKey() || !property.IsColumnNullable(tableIdentifier))
                {
                    continue;
                }

                var columnName = property.GetColumnName(tableIdentifier);
                var columnType = property.GetColumnType(tableIdentifier);
                if (columnName is null || columnType is null)
                {
                    continue;
                }

                yield return $"ALTER TABLE {qualifiedTable} ADD COLUMN IF NOT EXISTS {sql.DelimitIdentifier(columnName)} {columnType} NULL";
            }
        }
    }

    /// <summary>Creates every index of the model that does not exist yet (idempotent; PostgreSQL and SQLite).</summary>
    internal static async Task EnsureIndexesAsync(WorkflowDbContext db, CancellationToken cancellationToken)
    {
        foreach (var statement in EnsureIndexStatements(db))
        {
#pragma warning disable EF1002 // identifiers come from the EF model (delimited), not from user input
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
#pragma warning restore EF1002
        }
    }

    internal static IEnumerable<string> EnsureIndexStatements(WorkflowDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.Ordinal) && !provider.Contains("Sqlite", StringComparison.Ordinal))
        {
            yield break;
        }

        var sql = db.GetService<ISqlGenerationHelper>();
        // Index metadata such as filters and sort order is only kept in the design-time model.
        foreach (var entityType in db.GetService<IDesignTimeModel>().Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            var schema = entityType.GetSchema();
            var tableIdentifier = StoreObjectIdentifier.Table(table, schema);
            var qualifiedTable = sql.DelimitIdentifier(table, schema);
            foreach (var index in entityType.GetIndexes())
            {
                var name = index.GetDatabaseName(tableIdentifier);
                if (name is null)
                {
                    continue;
                }

                var columns = string.Join(", ", index.Properties.Select((p, i) =>
                    sql.DelimitIdentifier(p.GetColumnName(tableIdentifier)!) + (index.IsDescending is { Count: > 0 } desc && desc[i] ? " DESC" : string.Empty)));
                yield return $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS "
                    + $"{sql.DelimitIdentifier(name)} ON {qualifiedTable} ({columns})"
                    + (index.GetFilter() is { Length: > 0 } filter ? $" WHERE {filter}" : string.Empty);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> TablesExistAsync(WorkflowDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'WorkflowInstances'").SingleAsync(cancellationToken) > 0;
        }

        var schema = options.Value.Schema ?? "public";
        return await db.Database.SqlQuery<int>($"SELECT CAST(COUNT(*) AS int) AS \"Value\" FROM information_schema.tables WHERE table_schema = {schema} AND table_name = 'WorkflowInstances'").SingleAsync(cancellationToken) > 0;
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
        if (provider.Contains("Npgsql", StringComparison.Ordinal))
        {
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
        }
#pragma warning restore EF1002
    }
}
