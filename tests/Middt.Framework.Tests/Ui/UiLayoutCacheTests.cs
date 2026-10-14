using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Middt.Framework.Tests.Ui;

/// <summary>Snapshot reload guard of the layout store and the cached layout JSON schema.</summary>
public sealed class UiLayoutCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_reads_after_expiry_load_the_layouts_once()
    {
        var counter = new LayoutQueryCounter();
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        await using var _ = connection;
        await using var host = await UiTestHost.StartAsync(
            [UiTestHost.Layout("orders"), UiTestHost.Layout("customers")],
            o => o.UseDbContext(db => db.UseSqlite(connection).AddInterceptors(counter)),
            connection);
        await host.WithStoreAsync(s =>
        {
            s.Invalidate();
            return Task.FromResult(0);
        });
        counter.Reset();

        var reads = Enumerable.Range(0, 20).Select(i => host.WithStoreAsync(s => s.FindAsync(i % 2 == 0 ? "orders" : "customers", Ct)));
        var results = await Task.WhenAll(reads);

        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        counter.LayoutQueries.Should().Be(1, "one request reloads the snapshot, the others wait for it");
    }

    [Fact]
    public async Task Writes_invalidate_the_snapshot()
    {
        await using var host = await UiTestHost.StartAsync([UiTestHost.Layout("orders")]);
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct))).Should().NotBeNull();

        await host.WithStoreAsync(s => s.DeleteAsync("orders", null, "admin", Ct));

        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct))).Should().BeNull();
    }

    [Fact]
    public async Task Schema_endpoint_returns_the_same_document_on_every_call()
    {
        await using var host = await UiTestHost.StartAsync();

        using var first = await host.SendAsync(HttpMethod.Get, "api/ui/schema");
        using var second = await host.SendAsync(HttpMethod.Get, "api/ui/schema");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("application/schema+json");
        (await second.Content.ReadAsStringAsync(Ct)).Should().Be(await first.Content.ReadAsStringAsync(Ct));
    }

    private sealed class LayoutQueryCounter : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> texts = new();

        public int LayoutQueries => texts.Count(t => t.StartsWith("SELECT", StringComparison.Ordinal) && t.Contains("FROM \"UiLayouts\"", StringComparison.Ordinal));

        public void Reset() => texts.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            texts.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
