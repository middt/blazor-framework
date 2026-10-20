using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Middt.Framework.Tests.Workflow.Postgres;

/// <summary>
/// One PostgreSQL container (Testcontainers) shared by every test of the <c>Postgres</c> collection. Each test host gets its
/// own schema, so tests stay independent on one server; two hosts on the same schema behave like two nodes of one
/// application. Without Docker the tests are skipped (with the reason) rather than failed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await container.StartAsync();
            ConnectionString = container.GetConnectionString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Unavailable = $"PostgreSQL container could not start (Docker available?): {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>Skips the calling test when the container is not running.</summary>
    public void RequireDatabase()
    {
        if (Unavailable is not null)
        {
            Assert.Skip(Unavailable);
        }
    }

    public static string NewSchema() => "t_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Runs <paramref name="sql"/> on the host's connection and returns the first column of every row as text.</summary>
    public static Task<List<string>> QueryAsync(WorkflowTestHost host, string sql) =>
        host.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            try
            {
                return await ReadAsync(connection, sql);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        });

    public static async Task<List<string>> ReadAsync(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return rows;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
