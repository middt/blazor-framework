using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class JsonPatchTests
{
    [Fact]
    public void Diff_then_apply_reproduces_the_target()
    {
        var from = JsonNode.Parse("""{ "a": 1, "keep": "x", "gone": true, "nested": { "b": 2, "c": [1, 2] }, "n": null }""")!.AsObject();
        var to = JsonNode.Parse("""{ "a": 2, "keep": "x", "nested": { "b": 2, "c": [1, 2, 3], "d": { "e": "f" } }, "n": null, "added": null, "a/b~c": 1 }""")!.AsObject();

        var patch = JsonPatch.Diff(from, to);
        var replayed = (JsonObject)from.DeepClone();
        JsonPatch.Apply(replayed, patch);

        JsonNode.DeepEquals(replayed, to).Should().BeTrue();
        patch.Select(op => op!["path"]!.GetValue<string>()).Should().BeEquivalentTo(["/a", "/gone", "/nested/c", "/nested/d", "/added", "/a~1b~0c"]);
    }

    [Fact]
    public void Equal_documents_give_an_empty_patch()
    {
        var data = JsonNode.Parse("""{ "a": { "b": [1, { "c": 2 }] } }""")!.AsObject();
        JsonPatch.Diff(data, (JsonObject)data.DeepClone()).Should().BeEmpty();
    }

    [Fact]
    public void Apply_rejects_unknown_operations()
    {
        var act = () => JsonPatch.Apply([], JsonNode.Parse("""[{ "op": "move", "path": "/a", "from": "/b" }]""")!.AsArray());
        act.Should().Throw<FormatException>();
    }
}

public sealed class WorkflowHistoryModeTests
{
    private static string Flow(string key, string history, int snapshotEvery = 20) => $$"""
        {
          "key": "{{key}}", "history": "{{history}}", "historySnapshotEvery": {{snapshotEvery}},
          "start": { "key": "start", "target": "draft",
            "input": { "schema": { "type": "object", "properties": { "name": { "type": "string" }, "bio": { "type": "string" } } } } },
          "states": [
            { "key": "draft", "transitions": [
                { "key": "edit", "target": "$self",
                  "input": { "schema": { "type": "object", "properties": { "note": { "type": "string" }, "drop": { "type": "boolean" } } }, "into": "edit" } },
                { "key": "clear", "target": "$self", "input": { "set": { "edit": "null" } } },
                { "key": "finish", "target": "done" } ] },
            { "key": "done", "type": "Finish" }
          ]
        }
        """;

    private static async Task<(WorkflowTestHost Host, Guid Id)> DriveAsync(string key, string history)
    {
        var host = await WorkflowTestHost.StartAsync(Flow(key, history));
        // Realistic data size: a patch is only kept when it is smaller than the data.
        var instance = await host.StartInstanceAsync(key, new { name = "a", bio = new string('x', 400) });
        await host.TransitionAsync(instance.Id, "edit", new { note = "first" });
        await host.TransitionAsync(instance.Id, "edit", new { note = "second" });
        await host.TransitionAsync(instance.Id, "clear");
        await host.TransitionAsync(instance.Id, "finish");
        return (host, instance.Id);
    }

    private static Task<List<(string TransitionKey, int DataVersion, JsonObject Data)>> ReplayAsync(WorkflowTestHost host, Guid id) =>
        host.WithEngineAsync(e => e.HistoryDataAsync(id, WorkflowUser.System, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Delta_rows_store_patches_and_replay_to_the_same_data_as_full_snapshots()
    {
        var (full, fullId) = await DriveAsync("full-flow", "full");
        await using var _ = full;
        var (delta, deltaId) = await DriveAsync("delta-flow", "delta");
        await using var __ = delta;

        var deltaRows = await delta.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == deltaId).OrderBy(h => h.Id).ToListAsync(TestContext.Current.CancellationToken));
        deltaRows.Select(Kind).Should().Equal(["snapshot", "patch", "patch", "patch", "snapshot"],
            "the first row and the row that finishes the instance are snapshots");
        JsonNode.Parse(deltaRows[2].Patch!)!.AsArray().Should().ContainSingle().Which!["path"]!.GetValue<string>().Should().Be("/edit/note");

        var fullRows = await full.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == fullId).ToListAsync(TestContext.Current.CancellationToken));
        fullRows.Should().OnlyContain(h => h.Data != null && h.Patch == null);

        var fromFull = await ReplayAsync(full, fullId);
        var fromDelta = await ReplayAsync(delta, deltaId);
        fromDelta.Select(r => (r.TransitionKey, r.DataVersion)).Should().Equal(fromFull.Select(r => (r.TransitionKey, r.DataVersion)));
        for (var i = 0; i < fromFull.Count; i++)
        {
            JsonNode.DeepEquals(fromDelta[i].Data, fromFull[i].Data).Should().BeTrue($"row {i} ({fromFull[i].TransitionKey})");
        }

        fromDelta[^1].Data.ToJsonString().Should().Be((await delta.GetAsync(deltaId)).Data.ToJsonString());
    }

    private static string Kind(Middt.Framework.Workflow.Persistence.WorkflowHistory row) =>
        row is { Data: not null, Patch: null } ? "snapshot" : row is { Data: null, Patch: not null } ? "patch" : "invalid";

    [Fact]
    public async Task Delta_writes_a_snapshot_every_n_rows_so_a_replay_never_needs_more_than_n_patches()
    {
        await using var host = await WorkflowTestHost.StartAsync(Flow("checkpoint-flow", "delta", snapshotEvery: 3));
        var instance = await host.StartInstanceAsync("checkpoint-flow", new { name = "a", bio = new string('x', 400) });
        for (var i = 1; i <= 7; i++)
        {
            await host.TransitionAsync(instance.Id, "edit", new { note = $"n{i}" });
        }

        var rows = await host.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == instance.Id).OrderBy(h => h.Id).ToListAsync(TestContext.Current.CancellationToken));
        rows.Select(Kind).Should().Equal(["snapshot", "patch", "patch", "snapshot", "patch", "patch", "snapshot", "patch"]);
        (await host.RowAsync(instance.Id)).HistoryPatchCount.Should().Be(1);

        var replay = await ReplayAsync(host, instance.Id);
        replay[^1].Data["edit"]!["note"]!.GetValue<string>().Should().Be("n7");
        replay[2].Data["edit"]!["note"]!.GetValue<string>().Should().Be("n2");
    }

    [Fact]
    public async Task Delta_writes_a_snapshot_when_the_patch_would_not_be_smaller_than_the_data()
    {
        await using var host = await WorkflowTestHost.StartAsync(Flow("small-flow", "delta"));
        var instance = await host.StartInstanceAsync("small-flow", new { name = "a" });
        await host.TransitionAsync(instance.Id, "edit", new { note = "this note is longer than the rest of the data" });

        var rows = await host.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == instance.Id).OrderBy(h => h.Id).ToListAsync(TestContext.Current.CancellationToken));
        rows.Select(Kind).Should().Equal(["snapshot", "snapshot"]);
    }

    [Fact]
    public async Task HistorySnapshotEvery_must_be_positive()
    {
        var act = () => WorkflowTestHost.StartAsync(Flow("bad-flow", "delta", snapshotEvery: 0));
        (await act.Should().ThrowAsync<WorkflowDefinitionException>()).Which.Message.Should().Contain("historySnapshotEvery must be ≥ 1");
    }

    [Fact]
    public async Task None_writes_no_history_rows_but_the_instance_data_is_kept()
    {
        var (host, id) = await DriveAsync("quiet-flow", "none");
        await using var _ = host;

        (await host.HistoryAsync(id)).Should().BeEmpty();
        (await ReplayAsync(host, id)).Should().BeEmpty();
        var instance = await host.GetAsync(id);
        instance.State.Should().Be("done");
        instance.DataVersion.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Default_mode_is_full()
    {
        var host = await WorkflowTestHost.StartAsync(Flow("default-flow", "full").Replace("\"history\": \"full\", ", string.Empty, StringComparison.Ordinal));
        await using var _ = host;
        var instance = await host.StartInstanceAsync("default-flow", new { name = "a" });
        (await host.RowAsync(instance.Id)).HistoryPatchCount.Should().BeNull("only delta keeps the counter");

        var row = await host.WithDbAsync(db => db.History.AsNoTracking().SingleAsync(h => h.InstanceId == instance.Id, TestContext.Current.CancellationToken));
        row.Data.Should().NotBeNull();
        row.Patch.Should().BeNull();
    }
}
