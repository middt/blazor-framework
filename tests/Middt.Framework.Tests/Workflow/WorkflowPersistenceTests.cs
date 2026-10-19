using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Tests.Workflow;

/// <summary>Database round trips of commands, index maintenance and key generation.</summary>
public sealed class WorkflowPersistenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Simple = """
        {
          "key": "simple",
          "start": { "key": "start", "target": "a" },
          "states": [
            { "key": "a", "transitions": [ { "key": "next", "target": "b" }, { "key": "stay", "target": "$self" } ] },
            { "key": "b", "transitions": [
                { "key": "back", "target": "a" },
                { "key": "later", "target": "c", "trigger": "Scheduled", "after": "PT1H" } ] },
            { "key": "c", "type": "Finish" }
          ]
        }
        """;

    private static async Task<(WorkflowTestHost Host, CommandCounter Counter)> HostAsync()
    {
        var counter = new CommandCounter();
        var host = await WorkflowTestHost.StartAsync([Simple], configureDb: db => db.AddInterceptors(counter));
        return (host, counter);
    }

    [Fact]
    public async Task Transition_endpoint_needs_few_statements_and_returns_the_new_state()
    {
        var (host, counter) = await HostAsync();
        await using var _ = host;
        var instance = await host.StartInstanceAsync("simple");

        counter.Reset();
        using var request = host.Request(HttpMethod.Post, $"api/workflows/simple/instances/{instance.Id}/transitions/next");
        using var response = await host.Client.SendAsync(request, Ct);

        response.EnsureSuccessStatusCode();
        // Engine: read + claim + delete left-state jobs + save (SQLite runs the update/inserts as separate commands; PostgreSQL
        // batches them). No second read of the instance and no job query for the response (formerly 11 commands here).
        // The test host's RecordingEventHandler adds one SELECT of its own. Durable event delivery adds one job insert
        // in the state transaction and one delete after successful delivery.
        var log = string.Join(Environment.NewLine, counter.Texts);
        counter.Commands.Should().BeLessThanOrEqualTo(9, log);
        counter.Texts.Count(t => t.StartsWith("SELECT", StringComparison.Ordinal) && t.Contains("FROM \"WorkflowInstances\"", StringComparison.Ordinal))
            .Should().Be(2, log);
        counter.Texts.Should().NotContain(t => t.StartsWith("SELECT", StringComparison.Ordinal) && t.Contains("FROM \"WorkflowJobs\"", StringComparison.Ordinal), log);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("\"state\":\"b\"").And.Contain("\"later\"", "the armed scheduled transition is part of the response");
    }

    [Fact]
    public async Task Responses_of_commands_match_a_fresh_read()
    {
        var (host, _) = await HostAsync();
        await using var __ = host;
        var instance = await host.StartInstanceAsync("simple");

        foreach (var transition in new[] { "next", "back", "stay", "next" })
        {
            var model = await host.TransitionAsync(instance.Id, transition);
            var fresh = await host.GetAsync(instance.Id, WorkflowTestHost.User());

            model.Should().BeEquivalentTo(fresh, o => o.Excluding(m => m.Data), transition);
            model.Data.ToJsonString().Should().Be(fresh.Data.ToJsonString());
        }

        var back = await host.TransitionAsync(instance.Id, "back");
        back.ScheduledTransitions.Should().BeEmpty("the jobs of the left state were deleted");
        (await host.JobsAsync(instance.Id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Scheduled);
    }

    [Fact]
    public async Task Self_transition_keeps_the_scheduled_jobs_of_the_current_state_in_the_response()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "self", "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "transitions": [
                  { "key": "stay", "target": "$self" },
                  { "key": "later", "target": "b", "trigger": "Scheduled", "after": "PT1H" } ] },
                { "key": "b", "type": "Finish" } ] }
            """);
        var instance = await host.StartInstanceAsync("self");
        instance.ScheduledTransitions.Should().ContainSingle();

        var stayed = await host.TransitionAsync(instance.Id, "stay");

        stayed.ScheduledTransitions.Should().ContainSingle().Which.Key.Should().Be("later");
    }

    [Fact]
    public async Task Command_validated_against_an_older_revision_is_rejected()
    {
        var bump = new RevisionBumpBeforeClaim();
        await using var host = await WorkflowTestHost.StartAsync([Simple], configureDb: db => db.AddInterceptors(bump));
        var instance = await host.StartInstanceAsync("simple");

        // Another node changes the instance between the read (validation) and the claim of this command.
        bump.Armed = true;
        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "next"))
            .Should().ThrowAsync<Middt.Framework.Workflow.Runtime.WorkflowConflictException>().WithMessage("*changed concurrently*");

        var row = await host.RowAsync(instance.Id);
        row.Status.Should().Be(WorkflowInstanceStatus.Active);
        row.State.Should().Be("a");
    }

    [Fact]
    public async Task Missing_indexes_are_added_to_existing_tables_at_startup()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        await using var ___ = connection;

        await using (var first = await WorkflowTestHost.StartAsync([Simple], connection: connection))
        {
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_WorkflowInstances_WorkflowKey_CreatedAt\"", Ct));
        }

        await using var second = await WorkflowTestHost.StartAsync([Simple], connection: connection);

        var indexes = await second.WithDbAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'index' AND tbl_name = 'WorkflowInstances'")
            .ToListAsync(Ct));
        indexes.Should().Contain(["IX_WorkflowInstances_WorkflowKey_CreatedAt", "IX_WorkflowInstances_Status_BusySince", "IX_WorkflowInstances_WorkflowKey_StartedBy_CreatedAt"]);
    }

    [Fact]
    public async Task Generated_keys_are_ordered_by_time_in_uuid_v7_order()
    {
        var ids = new List<Guid>(20);
        for (var i = 0; i < 20; i++)
        {
            ids.Add(Guid.CreateVersion7());
            await Task.Delay(2, Ct);
        }

        // Standard big-endian byte order (used by PostgreSQL uuid B-tree indexes) orders UUIDv7 chronologically.
        var sorted = ids.OrderBy(g => Convert.ToHexString(g.ToByteArray(bigEndian: true)), StringComparer.Ordinal).ToList();
        sorted.Should().Equal(ids);
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Busy_recovery_runs_at_most_once_per_interval()
    {
        await using var host = await WorkflowTestHost.StartAsync([Simple], configure: o =>
        {
            o.BusyTimeout = TimeSpan.FromMinutes(1);
            o.BusyRecoveryInterval = TimeSpan.FromMinutes(10);
        });
        var instance = await host.StartInstanceAsync("simple");

        await host.RunJobsAsync(); // first scan happens right away
        await host.SetStatusAsync(instance.Id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.RunJobsAsync(TimeSpan.FromMinutes(5));
        (await host.RowAsync(instance.Id)).Status.Should().Be(WorkflowInstanceStatus.Busy, "the next scan is not due yet");

        await host.RunJobsAsync(TimeSpan.FromMinutes(6));
        (await host.RowAsync(instance.Id)).Status.Should().Be(WorkflowInstanceStatus.Faulted);
    }

    [Fact]
    public async Task Instance_data_is_persisted_in_dedicated_WorkflowInstanceData_table()
    {
        await using var host = await WorkflowTestHost.StartAsync([TestWorkflows.Pipeline]);
        var started = await host.StartInstanceAsync("pipeline", new { name = "bike", amount = 100 }, user: WorkflowTestHost.User("alice"));

        var (instanceCols, dataCols, dataRow) = await host.WithDbAsync(async db =>
        {
            var instCols = await db.Database
                .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('WorkflowInstances') ORDER BY name")
                .ToListAsync(Ct);
            var dtCols = await db.Database
                .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('WorkflowInstanceData') ORDER BY name")
                .ToListAsync(Ct);
            var row = await db.InstanceData.AsNoTracking().SingleAsync(d => d.InstanceId == started.Id, Ct);
            return (instCols, dtCols, row);
        });

        instanceCols.Should().NotContain(["Data", "DataVersion"]);
        dataCols.Should().BeEquivalentTo(["Data", "DataHash", "DataVersion", "ETag", "InstanceId", "UpdatedAt", "WorkflowKey"]);
        dataRow.WorkflowKey.Should().Be("pipeline");
        dataRow.DataVersion.Should().Be(1);
        dataRow.Data.Should().Contain("\"bike\"");
        dataRow.DataHash.Should().Be(WorkflowInstanceData.ComputeHash(dataRow.Data));
        dataRow.ETag.Should().Be(WorkflowInstanceData.NewETag(started.Id, 1));
    }

    [Fact]
    public async Task Flow_specific_indexes_and_workflow_keys_are_created_for_registered_workflows()
    {
        await using var host = await WorkflowTestHost.StartAsync([TestWorkflows.Pipeline]);
        var started = await host.StartInstanceAsync("pipeline", new { name = "road-bike", amount = 250 }, user: WorkflowTestHost.User("alice"));

        var (indexes, hist) = await host.WithDbAsync(async db =>
        {
            var idxs = await db.Database
                .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'index'")
                .ToListAsync(Ct);

            var h = await db.History.AsNoTracking().Where(item => item.InstanceId == started.Id).ToListAsync(Ct);
            return (idxs, h);
        });

        indexes.Should().Contain(name => name.Contains("IX_Part_Pipeline"));
        hist.Should().NotBeEmpty();
        hist.Should().AllSatisfy(h => h.WorkflowKey.Should().Be("pipeline"));
    }

    /// <summary>Simulates a concurrent writer: increments the revision right before the next claim UPDATE runs.</summary>
    private sealed class RevisionBumpBeforeClaim : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("UPDATE \"WorkflowInstances\"", StringComparison.Ordinal) && command.CommandText.Contains("BusySince", StringComparison.Ordinal))
            {
                Armed = false;
                await using var bump = command.Connection!.CreateCommand();
                bump.CommandText = "UPDATE \"WorkflowInstances\" SET \"Revision\" = \"Revision\" + 1";
                await bump.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Counts executed commands (and keeps their text).</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> texts = new();

        public int Commands => texts.Count;

        public IReadOnlyList<string> Texts => [.. texts];

        public void Reset() => texts.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            texts.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            texts.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            texts.Enqueue(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
