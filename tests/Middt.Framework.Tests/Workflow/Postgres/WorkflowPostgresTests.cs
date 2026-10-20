using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using Npgsql;

namespace Middt.Framework.Tests.Workflow.Postgres;

/// <summary>
/// Behaviour only a real PostgreSQL can show: list partitioning per workflow, jsonb GIN indexes, two nodes racing for one
/// instance, <c>FOR UPDATE SKIP LOCKED</c> job claims, <c>LISTEN</c>/<c>NOTIFY</c> wake-ups, and the schema upgrade of
/// existing tables.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkflowPostgresTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Ledger = """
        {
          "key": "ledger", "history": "delta",
          "start": { "key": "start", "target": "open",
            "input": { "schema": { "type": "object", "properties": { "customer": { "type": "object" }, "amount": { "type": "number" } } } } },
          "states": [
            { "key": "open", "transitions": [
                { "key": "advance", "target": "closed", "onExecute": [ { "task": "record", "config": { "name": "advance" } } ] },
                { "key": "go", "target": "closed", "onExecute": [ { "task": "gate" } ] },
                { "key": "touch", "target": "$self", "input": { "schema": { "type": "object", "properties": { "n": { "type": "number" } } }, "into": "touch" } } ] },
            { "key": "closed", "type": "Finish" }
          ]
        }
        """;

    private const string Other = """
        { "key": "other", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "type": "Finish" } ] }
        """;

    private Task<WorkflowTestHost> StartAsync(string schema, TaskGate? gate = null, FakeTimeProvider? time = null, Action<IServiceCollection>? services = null, params string[] definitions)
    {
        postgres.RequireDatabase();
        return WorkflowTestHost.StartPostgresAsync(postgres.ConnectionString, schema, definitions.Length > 0 ? definitions : [Ledger, Other, WorkflowMessageTests.Order],
            o => o.AddTask<GateTask>("gate"),
            s =>
            {
                s.AddSingleton(gate ?? new TaskGate());
                services?.Invoke(s);
            },
            time);
    }

    [Fact]
    public async Task Each_workflow_gets_its_own_partition_and_its_rows_land_there()
    {
        var schema = PostgresFixture.NewSchema();
        await using var host = await StartAsync(schema);

        var ledger = await host.StartInstanceAsync("ledger", new { amount = 1 });
        var other = await host.StartInstanceAsync("other");

        var partitions = await PostgresFixture.QueryAsync(host, $"""
            SELECT c.relname FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid JOIN pg_class p ON p.oid = i.inhparent
            JOIN pg_namespace n ON n.oid = p.relnamespace
            WHERE n.nspname = '{schema}' AND p.relname = 'WorkflowInstances'
            """);
        partitions.Should().Contain(["WorkflowInstances_Default", $"WorkflowInstances_{WorkflowPartitionManager.PartitionSuffix("ledger")}", $"WorkflowInstances_{WorkflowPartitionManager.PartitionSuffix("other")}"]);

        (await PostgresFixture.QueryAsync(host, $"""SELECT tableoid::regclass::text FROM "{schema}"."WorkflowInstances" WHERE "Id" = '{ledger.Id}'"""))
            .Should().ContainSingle().Which.Should().Contain($"WorkflowInstances_{WorkflowPartitionManager.PartitionSuffix("ledger")}");
        (await PostgresFixture.QueryAsync(host, $"""SELECT tableoid::regclass::text FROM "{schema}"."WorkflowInstanceData" WHERE "InstanceId" = '{other.Id}'"""))
            .Should().ContainSingle().Which.Should().Contain($"WorkflowInstanceData_{WorkflowPartitionManager.PartitionSuffix("other")}");
    }

    [Fact]
    public async Task Two_workflow_schemas_in_one_process_each_use_their_own_tables()
    {
        var schemaA = PostgresFixture.NewSchema();
        var schemaB = PostgresFixture.NewSchema();
        await using var hostA = await StartAsync(schemaA);
        await using var hostB = await StartAsync(schemaB);

        await hostA.StartInstanceAsync("other");
        await hostB.StartInstanceAsync("other");
        await hostB.StartInstanceAsync("other");

        // EF caches the model per context type; the schema has to be part of that cache key.
        (await PostgresFixture.QueryAsync(hostA, $"""SELECT count(*) FROM "{schemaA}"."WorkflowInstances" """)).Should().Equal("1");
        (await PostgresFixture.QueryAsync(hostA, $"""SELECT count(*) FROM "{schemaB}"."WorkflowInstances" """)).Should().Equal("2");
    }

    [Fact]
    public async Task The_jsonb_GIN_index_serves_containment_queries_on_instance_data()
    {
        var schema = PostgresFixture.NewSchema();
        await using var host = await StartAsync(schema);
        for (var i = 0; i < 30; i++)
        {
            await host.StartInstanceAsync("ledger", new { customer = new { tier = i % 3 == 0 ? "gold" : "basic" }, amount = i });
        }

        var indexes = await PostgresFixture.QueryAsync(host, $"""SELECT indexdef FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = 'WorkflowInstanceData_{WorkflowPartitionManager.PartitionSuffix("ledger")}'""");
        indexes.Should().Contain(d => d.Contains("USING gin", StringComparison.Ordinal) && d.Contains("jsonb_path_ops", StringComparison.Ordinal));

        var partition = $"WorkflowInstanceData_{WorkflowPartitionManager.PartitionSuffix("ledger")}";
        var (containment, pruning) = await host.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(Ct);
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            var connection = db.Database.GetDbConnection();
            var tx = transaction.GetDbTransaction();
            await PostgresFixture.ReadAsync(connection, "SET LOCAL enable_seqscan = off", tx);
            await PostgresFixture.ReadAsync(connection, $"""ANALYZE "{schema}"."{partition}" """, tx);
            var containmentPlan = await PostgresFixture.ReadAsync(connection, $"""
                EXPLAIN SELECT "InstanceId" FROM "{schema}"."{partition}" WHERE "Data" @> '{"{"}"customer": {"{"}"tier": "gold"{"}"}{"}"}'
                """, tx);
            var pruningPlan = await PostgresFixture.ReadAsync(connection, $"""
                EXPLAIN SELECT "InstanceId" FROM "{schema}"."WorkflowInstanceData" WHERE "WorkflowKey" = 'ledger'
                """, tx);
            return (string.Join('\n', containmentPlan), string.Join('\n', pruningPlan));
        });

        // The containment filter can only be answered by the jsonb_path_ops GIN index (btrees cannot serve @>).
        var usedIndex = System.Text.RegularExpressions.Regex.Match(containment, "Bitmap Index Scan on \"?([^\" ]+)\"?").Groups[1].Value;
        usedIndex.Should().NotBeEmpty(containment);
        (await PostgresFixture.QueryAsync(host, $"""SELECT indexdef FROM pg_indexes WHERE schemaname = '{schema}' AND indexname = '{usedIndex}'"""))
            .Should().ContainSingle().Which.Should().Contain("USING gin").And.Contain("jsonb_path_ops");

        // Filtering by workflow touches only that workflow's partition.
        pruning.Should().Contain(partition).And.NotContain("_Default").And.NotContain($"WorkflowInstanceData_{WorkflowPartitionManager.PartitionSuffix("other")}");

        var gold = await PostgresFixture.QueryAsync(host, $"""SELECT count(*) FROM "{schema}"."WorkflowInstanceData" WHERE "WorkflowKey" = 'ledger' AND "Data" @> '{"{"}"customer": {"{"}"tier": "gold"{"}"}{"}"}'""");
        gold.Should().Equal("10");
    }

    [Fact]
    public async Task Two_nodes_racing_for_one_instance_let_exactly_one_claim_win()
    {
        var schema = PostgresFixture.NewSchema();
        var gate = new TaskGate { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var nodeA = await StartAsync(schema, gate);
        await using var nodeB = await StartAsync(schema, gate);
        var instance = await nodeA.StartInstanceAsync("ledger");

        var attempts = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => (i % 2 == 0 ? nodeA : nodeB).TransitionAsync(instance.Id, "go"), Ct))
            .ToList();

        // Exactly one claim gets in and holds the instance (the gate); every other attempt must be refused meanwhile.
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var refused = 0;
        var pending = attempts.ToList();
        while (refused < 9)
        {
            var finished = await Task.WhenAny(pending).WaitAsync(TimeSpan.FromSeconds(30), Ct);
            pending.Remove(finished);
            finished.IsFaulted.Should().BeTrue("only the holder of the claim may proceed");
            finished.Exception!.InnerException.Should().BeOfType<WorkflowClaimException>();
            refused++;
        }

        gate.Hold!.TrySetResult();
        var winner = await pending.Single();
        winner.State.Should().Be("closed");
        gate.Calls.Should().Be(1);
        (await nodeB.HistoryAsync(instance.Id)).Count(h => h.TransitionKey == "go").Should().Be(1);
    }

    [Fact]
    public async Task Job_claims_skip_rows_locked_by_another_node_instead_of_waiting()
    {
        var schema = PostgresFixture.NewSchema();
        await using var host = await StartAsync(schema);
        var first = await host.StartInstanceAsync("ledger");
        var second = await host.StartInstanceAsync("ledger");
        await host.TransitionAsync(first.Id, "advance", runAsync: true);
        await host.TransitionAsync(second.Id, "advance", runAsync: true);
        var lockedJob = (await host.JobsAsync(first.Id)).Single().Id;

        // Another node is in the middle of claiming the first job: its row is locked.
        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync(Ct);
        await using var transaction = await other.BeginTransactionAsync(Ct);
        await PostgresFixture.ReadAsync(other, $"""SELECT "Id" FROM "{schema}"."WorkflowJobs" WHERE "Id" = '{lockedJob}' FOR UPDATE""", transaction);

        var stopwatch = Stopwatch.StartNew();
        var processed = await host.Processor.RunOnceAsync(Ct).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        stopwatch.Stop();

        processed.Should().Be(1, "the locked job is skipped, the other one is taken");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "SKIP LOCKED never waits for the other node's lock");
        (await host.GetAsync(second.Id)).State.Should().Be("closed");
        (await host.GetAsync(first.Id)).State.Should().Be("open");

        await transaction.RollbackAsync(Ct);
        (await host.RunJobsAsync()).Should().Be(1);
        (await host.GetAsync(first.Id)).State.Should().Be("closed");
    }

    [Fact]
    public async Task Concurrent_processors_on_two_nodes_run_every_job_exactly_once()
    {
        var schema = PostgresFixture.NewSchema();
        await using var nodeA = await StartAsync(schema);
        await using var nodeB = await StartAsync(schema);
        var ids = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            var instance = await nodeA.StartInstanceAsync("ledger");
            await nodeA.TransitionAsync(instance.Id, "advance", runAsync: true);
            ids.Add(instance.Id);
        }

        async Task<int> DrainAsync(WorkflowTestHost node)
        {
            var total = 0;
            int processed;
            do
            {
                processed = await node.Processor.RunOnceAsync(Ct);
                total += processed;
            }
            while (processed > 0);
            return total;
        }

        var counts = await Task.WhenAll(Task.Run(() => DrainAsync(nodeA), Ct), Task.Run(() => DrainAsync(nodeB), Ct));

        counts.Sum().Should().Be(40);
        nodeA.Log.Entries.Count(e => e == "advance").Should().Be(counts[0]);
        nodeB.Log.Entries.Count(e => e == "advance").Should().Be(counts[1]);
        var history = await nodeA.WithDbAsync(db => db.History.AsNoTracking().Where(h => ids.Contains(h.InstanceId) && h.TransitionKey == "advance")
            .GroupBy(h => h.InstanceId).Select(g => g.Count()).ToListAsync(Ct));
        history.Should().HaveCount(40).And.OnlyContain(c => c == 1);
    }

    [Fact]
    public async Task A_job_written_on_one_node_wakes_the_processor_of_another_node_through_NOTIFY()
    {
        var schema = PostgresFixture.NewSchema();
        await using var nodeA = await StartAsync(schema);
        await using var nodeB = await StartAsync(schema, services: s => s.AddSingleton<WorkflowJobListener>());
        var listener = nodeB.Services.GetRequiredService<WorkflowJobListener>();
        var signal = nodeB.Services.GetRequiredService<WorkflowJobSignal>();
        await listener.StartAsync(Ct);
        try
        {
            await listener.Listening.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            await signal.WaitAsync(TimeSpan.FromMilliseconds(300), Ct); // drain the wake-up sent after LISTEN

            var instance = await nodeA.StartInstanceAsync("ledger");
            await signal.WaitAsync(TimeSpan.FromMilliseconds(300), Ct); // starting writes no jobs, but drain anyway
            var stopwatch = Stopwatch.StartNew();
            await nodeA.TransitionAsync(instance.Id, "advance", runAsync: true);
            await signal.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "node B is woken by NOTIFY, not by its 5 s polling interval");
            (await nodeB.RunJobsAsync()).Should().Be(1);
            (await nodeB.GetAsync(instance.Id)).State.Should().Be("closed");
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Delta_history_keeps_patches_in_jsonb_and_existing_tables_are_upgraded_at_startup()
    {
        var schema = PostgresFixture.NewSchema();
        await using (var first = await StartAsync(schema))
        {
            // A database created before history modes existed: the snapshot column was NOT NULL.
            await PostgresFixture.QueryAsync(first, $"""ALTER TABLE "{schema}"."WorkflowHistory" ALTER COLUMN "Data" SET NOT NULL""");
        }

        await using var upgraded = await StartAsync(schema);
        var id = (await upgraded.StartInstanceAsync("ledger", new { amount = 5, customer = new { notes = new string('x', 300) } })).Id;
        await upgraded.TransitionAsync(id, "touch", new { n = 1 });
        await upgraded.TransitionAsync(id, "touch", new { n = 2 });

        (await PostgresFixture.QueryAsync(upgraded, $"""SELECT is_nullable FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = 'WorkflowHistory' AND column_name = 'Data'"""))
            .Should().Equal("YES");
        (await PostgresFixture.QueryAsync(upgraded, $"""SELECT coalesce(jsonb_typeof("Patch"), 'none') || ':' || coalesce(jsonb_typeof("Data"), 'none') FROM "{schema}"."WorkflowHistory" WHERE "InstanceId" = '{id}' ORDER BY "Id" """))
            .Should().Equal(["none:object", "array:none", "array:none"], "a snapshot first, then patches in jsonb");
        var replay = await upgraded.WithEngineAsync(e => e.HistoryDataAsync(id, Middt.Framework.Workflow.Expressions.WorkflowUser.System, Ct));
        replay[^1].Data["touch"]!["n"]!.GetValue<int>().Should().Be(2);
        replay[^1].Data["amount"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public async Task Buffered_messages_and_background_tasks_work_on_PostgreSQL()
    {
        var schema = PostgresFixture.NewSchema();
        await using var host = await StartAsync(schema, definitions: [WorkflowMessageTests.Order, BackgroundFlow]);

        var order = await host.StartInstanceAsync("order", new { total = 100 }, "ORD-PG");
        await host.TransitionAsync(order.Id, "hold");
        var buffered = await host.WithEngineAsync(e => e.DeliverMessageAsync(new WorkflowMessage
        {
            Name = "payment.received", Workflow = "order", Key = "ORD-PG", Data = new JsonObject { ["amount"] = 100 }, BufferFor = TimeSpan.FromMinutes(5),
        }, WorkflowTestHost.User(), Ct));
        buffered.Outcome.Should().Be(WorkflowMessageOutcome.Buffered);
        await host.TransitionAsync(order.Id, "resume");
        await host.RunJobsAsync();
        (await host.GetAsync(order.Id)).State.Should().Be("paid");

        var job = await host.StartInstanceAsync("bg");
        job.Status.Should().Be(WorkflowInstanceStatus.Active);
        await host.RunJobsAsync();
        var finished = await host.GetAsync(job.Id);
        finished.State.Should().Be("finished");
        finished.Data["result"]!.GetValue<string>().Should().Be("ok");
    }

    private const string BackgroundFlow = """
        {
          "key": "bg",
          "start": { "key": "start", "target": "working" },
          "states": [
            { "key": "working", "onEntry": [ { "task": "gate", "background": true } ],
              "transitions": [ { "key": "done", "target": "finished", "trigger": "Automatic", "rule": "data.result == \"ok\"" } ] },
            { "key": "finished", "type": "Finish" }
          ]
        }
        """;
}
