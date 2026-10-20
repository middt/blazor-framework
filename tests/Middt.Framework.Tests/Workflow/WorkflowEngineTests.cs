using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowEngineTests
{
    private static Task<WorkflowTestHost> Host(params string[] definitions) => WorkflowTestHost.StartAsync(definitions);

    private static readonly WorkflowUser Manager = WorkflowTestHost.User("mia", "manager");

    // ───────────── start & input ─────────────

    [Fact]
    public async Task Start_validates_input_merges_it_and_applies_set_expressions()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget", amount = 50 }, user: WorkflowTestHost.User("bob"));

        instance.State.Should().Be("draft");
        instance.StateTitle.Should().Be("Draft");
        instance.Status.Should().Be(WorkflowInstanceStatus.Active);
        instance.Data["name"]!.GetValue<string>().Should().Be("widget");
        instance.Data["amount"]!.GetValue<decimal>().Should().Be(50);
        instance.Data["createdBy"]!.GetValue<string>().Should().Be("bob");
        instance.Data["upper"]!.GetValue<string>().Should().Be("WIDGET");
        instance.StartedBy.Should().Be("bob");
        instance.DataVersion.Should().Be(1);
        instance.CreatedAt.Should().Be(WorkflowTestHost.StartTime.UtcDateTime);
    }

    [Fact]
    public async Task Start_with_invalid_input_throws_validation_errors_and_creates_nothing()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        var act = () => host.StartInstanceAsync("pipeline", new { name = "x", amount = -1, isAdmin = true });

        var errors = (await act.Should().ThrowAsync<WorkflowValidationException>()).Which.Errors;
        errors.Should().BeEquivalentTo("$.name: must be at least 2 characters.", "$.amount: must be ≥ 0.", "$.isAdmin: is not allowed.");
        (await host.WithDbAsync(db => db.Instances.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task Start_without_body_fails_required_validation()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        var act = () => host.StartInstanceAsync("pipeline");

        (await act.Should().ThrowAsync<WorkflowValidationException>()).Which.Errors.Should().Equal("$.name: is required.");
    }

    [Fact]
    public async Task Start_of_unknown_workflow_is_not_found()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        await FluentActions.Awaiting(() => host.StartInstanceAsync("nope")).Should().ThrowAsync<WorkflowNotFoundException>();
    }

    [Fact]
    public async Task Body_of_a_transition_without_declared_input_is_ignored()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        var moved = await host.TransitionAsync(instance.Id, "plain", new { name = "hacked", createdBy = "mallory", status = "Completed" });

        moved.State.Should().Be("review");
        moved.Data["name"]!.GetValue<string>().Should().Be("widget");
        moved.Data["createdBy"]!.GetValue<string>().Should().Be("alice");
        moved.Data.ContainsKey("status").Should().BeFalse();
    }

    [Fact]
    public async Task Transition_input_is_merged_into_the_declared_path()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        var moved = await host.TransitionAsync(instance.Id, "submit", new { comment = "please" });

        moved.Data["submission"]!["comment"]!.GetValue<string>().Should().Be("please");
        moved.Data.ContainsKey("comment").Should().BeFalse();
    }

    // ───────────── pipeline ordering ─────────────

    [Fact]
    public async Task Tasks_run_in_order_execute_exit_entry_and_when_skips()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var small = await host.StartInstanceAsync("pipeline", new { name = "small", amount = 5 });
        await host.TransitionAsync(small.Id, "submit");

        host.Log.Entries.Should().Equal("start.execute", "draft.entry", "submit.execute", "draft.exit", "review.entry");

        var big = await host.StartInstanceAsync("pipeline", new { name = "large", amount = 500 });
        await host.TransitionAsync(big.Id, "submit");
        host.Log.Entries.Skip(5).Should().Equal("start.execute", "draft.entry", "submit.execute", "draft.exit", "review.entry", "review.big");
    }

    [Fact]
    public async Task Self_transition_updates_data_without_exit_or_entry()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        var touched = await host.TransitionAsync(instance.Id, "touch", new { note = "hello" });

        touched.State.Should().Be("draft");
        touched.Data["notes"]!["note"]!.GetValue<string>().Should().Be("hello");
        touched.DataVersion.Should().Be(2);
        host.Log.Entries.Should().Equal("start.execute", "draft.entry", "touch.execute");
        (await host.HistoryAsync(instance.Id)).Last().Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "touch" && h.FromState == "draft" && h.ToState == "draft");
    }

    [Fact]
    public async Task Finish_state_completes_the_instance()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        var done = await host.TransitionAsync(instance.Id, "approve", user: Manager);

        done.State.Should().Be("approved");
        done.StateType.Should().Be("Finish");
        done.StateSubType.Should().Be("Success");
        done.Status.Should().Be(WorkflowInstanceStatus.Completed);
        done.CompletedAt.Should().NotBeNull();
        done.AvailableTransitions.Should().BeEmpty();
        host.Log.Entries.Should().EndWith(["draft.exit", "approved.entry"]);
    }

    [Fact]
    public async Task Finish_state_with_cancelled_subtype_cancels_the_instance()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        var cancelled = await host.TransitionAsync(instance.Id, "cancel", new { reason = "duplicate" });

        cancelled.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        cancelled.State.Should().Be("cancelled");
        cancelled.Data["cancellation"]!["reason"]!.GetValue<string>().Should().Be("duplicate");
    }

    [Theory]
    [InlineData(100, "approved", null)]
    [InlineData(2000, "approved", "medium")]
    [InlineData(9000, "review", null)]
    public async Task Automatic_transitions_chain_and_the_default_branch_is_evaluated_last(int amount, string expectedState, string? routedVia)
    {
        await using var host = await Host(TestWorkflows.Routing);

        var instance = await host.StartInstanceAsync("routing", new { amount });

        instance.State.Should().Be(expectedState);
        instance.Data["routedVia"]?.GetValue<string>().Should().Be(routedVia);
        var history = await host.HistoryAsync(instance.Id);
        history.Select(h => h.Trigger).Skip(1).Should().OnlyContain(t => t == "Automatic");
        history.First().Trigger.Should().Be("Start");
    }

    [Fact]
    public async Task Automatic_loop_is_capped_and_faults_the_instance_with_an_incident()
    {
        await using var host = await WorkflowTestHost.StartAsync([TestWorkflows.Loop], o => o.MaxAutomaticChainLength = 5);

        var instance = await host.StartInstanceAsync("loop");

        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.Error.Should().Contain("exceeded 5 steps");
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle().Which.Error.Should().Contain("loop?");
        (await host.HistoryAsync(instance.Id)).Should().HaveCount(6);
    }

    // ───────────── availability, roles, conflicts ─────────────

    [Fact]
    public async Task Transition_not_available_in_the_current_state_is_a_conflict()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "nope")).Should().ThrowAsync<WorkflowConflictException>();
        // automatic transitions cannot be executed manually
        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "auto")).Should().ThrowAsync<WorkflowConflictException>();
    }

    [Fact]
    public async Task Transition_roles_are_enforced_and_filter_available_transitions()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "approve")).Should().ThrowAsync<WorkflowForbiddenException>();

        var asUser = await host.WithEngineAsync(e => e.GetAsync(instance.Id, WorkflowTestHost.User(), TestContext.Current.CancellationToken));
        asUser.AvailableTransitions.Select(t => t.Key).Should().BeEquivalentTo("submit", "plain", "touch", "cancel");

        var asManager = await host.WithEngineAsync(e => e.GetAsync(instance.Id, Manager, TestContext.Current.CancellationToken));
        asManager.AvailableTransitions.Select(t => t.Key).Should().Contain("approve");
        asManager.AvailableTransitions.Single(t => t.Key == "submit").InputSchema.Should().NotBeNull();
        asManager.AvailableTransitions.Single(t => t.Key == "submit").Title.Should().Be("Submit");
    }

    [Fact]
    public async Task Workflow_roles_guard_start_read_list_and_transitions()
    {
        await using var host = await Host(TestWorkflows.Restricted);
        var clerk = WorkflowTestHost.User("carl", "clerk");
        var outsider = WorkflowTestHost.User("eve");

        await FluentActions.Awaiting(() => host.StartInstanceAsync("restricted", user: outsider)).Should().ThrowAsync<WorkflowForbiddenException>();

        var instance = await host.StartInstanceAsync("restricted", user: clerk);
        await FluentActions.Awaiting(() => host.GetAsync(instance.Id, outsider)).Should().ThrowAsync<WorkflowForbiddenException>();
        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "close", user: outsider)).Should().ThrowAsync<WorkflowForbiddenException>();
        await FluentActions.Awaiting(() => host.WithEngineAsync(e => e.ListAsync("restricted", outsider, null, null, null, 1, 10, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<WorkflowForbiddenException>();

        (await host.TransitionAsync(instance.Id, "close", user: clerk)).Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Terminal_instance_rejects_transitions()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        await host.TransitionAsync(instance.Id, "cancel");

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "submit")).Should().ThrowAsync<WorkflowConflictException>()
            .WithMessage("*Cancelled*");
    }

    [Fact]
    public async Task Busy_instance_rejects_transitions_and_stays_busy()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        await host.SetStatusAsync(instance.Id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "submit")).Should().ThrowAsync<WorkflowConflictException>()
            .WithMessage("*busy*");

        var row = await host.RowAsync(instance.Id);
        row.Status.Should().Be(WorkflowInstanceStatus.Busy);
        row.State.Should().Be("draft");
        (await host.GetAsync(instance.Id)).AvailableTransitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_instance_is_not_found()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        await FluentActions.Awaiting(() => host.GetAsync(Guid.NewGuid())).Should().ThrowAsync<WorkflowNotFoundException>();
        await FluentActions.Awaiting(() => host.TransitionAsync(Guid.NewGuid(), "submit")).Should().ThrowAsync<WorkflowNotFoundException>();
    }

    // ───────────── business key ─────────────

    [Fact]
    public async Task Start_with_existing_business_key_returns_the_existing_instance()
    {
        await using var host = await Host(TestWorkflows.Pipeline);

        var (first, created) = await host.StartWithFlagAsync("pipeline", new { name = "one" }, key: "order-1");
        var (second, createdAgain) = await host.StartWithFlagAsync("pipeline", new { name = "two" }, key: "order-1");

        created.Should().BeTrue();
        createdAgain.Should().BeFalse();
        second.Id.Should().Be(first.Id);
        second.Data["name"]!.GetValue<string>().Should().Be("one");
        second.Key.Should().Be("order-1");
        (await host.WithDbAsync(db => db.Instances.CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_start_with_the_same_key_returns_the_winner()
    {
        var race = new RaceInterceptor();
        await using var host = await WorkflowTestHost.StartAsync([TestWorkflows.Pipeline], configureDb: db => db.AddInterceptors(race));

        // A competing request inserts the same key after our duplicate check but before our insert.
        Guid? winnerId = null;
        race.OnFirstInsert = async () => winnerId = (await host.StartInstanceAsync("pipeline", new { name = "winner" }, key: "race")).Id;

        var (instance, created) = await host.StartWithFlagAsync("pipeline", new { name = "loser" }, key: "race");

        created.Should().BeFalse();
        winnerId.Should().NotBeNull();
        instance.Id.Should().Be(winnerId!.Value);
        instance.Data["name"]!.GetValue<string>().Should().Be("winner");
        (await host.WithDbAsync(db => db.Instances.CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task Same_business_key_is_allowed_in_different_workflows()
    {
        await using var host = await Host(TestWorkflows.Pipeline, TestWorkflows.Routing);

        var a = await host.StartInstanceAsync("pipeline", new { name = "abc" }, key: "k");
        var b = await host.StartInstanceAsync("routing", new { amount = 1 }, key: "k");

        a.Id.Should().NotBe(b.Id);
    }

    // ───────────── history & events ─────────────

    [Fact]
    public async Task History_records_every_transition_with_data_snapshots()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" }, user: WorkflowTestHost.User("bob"));
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.TransitionAsync(instance.Id, "submit", new { comment = "c" }, user: WorkflowTestHost.User("carol"));

        var history = await host.HistoryAsync(instance.Id);

        history.Should().HaveCount(2);
        history[0].Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "start" && h.FromState == null && h.ToState == "draft" && h.Trigger == "Start" && h.ExecutedBy == "bob" && h.DataVersion == 1);
        history[1].Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "submit" && h.FromState == "draft" && h.ToState == "review" && h.Trigger == "Manual" && h.ExecutedBy == "carol" && h.DataVersion == 2);
        history[1].StartedAt.Should().Be(WorkflowTestHost.StartTime.UtcDateTime.AddMinutes(5));

        var rows = await host.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == instance.Id).OrderBy(h => h.Id).ToListAsync(TestContext.Current.CancellationToken));
        JsonNode.Parse(rows[0].Data!)!["submission"].Should().BeNull();
        JsonNode.Parse(rows[1].Data!)!["submission"]!["comment"]!.GetValue<string>().Should().Be("c");
        rows[1].Input.Should().Be("""{"comment":"c"}""");
    }

    [Fact]
    public async Task Events_are_dispatched_after_commit()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        await host.TransitionAsync(instance.Id, "submit");
        await host.TransitionAsync(instance.Id, "approve", user: Manager);

        var received = host.Events.Received.Where(e => e.Event.InstanceId == instance.Id).ToList();

        received.Select(e => e.Event.Type).Should().Equal(WorkflowEventType.Started, WorkflowEventType.StateChanged, WorkflowEventType.StateChanged, WorkflowEventType.Completed);
        received[1].Event.State.Should().Be("draft");
        received[2].Event.FromState.Should().Be("draft");
        received[2].Event.Transition.Should().Be("submit");
        received[3].Event.Workflow.Should().Be("pipeline");

        // The handler sees the committed row: the change is visible and the instance is no longer Busy.
        received.Skip(1).Should().AllSatisfy(e =>
        {
            e.CommittedState.Should().Be(e.Event.State);
            e.CommittedStatus.Should().NotBe(WorkflowInstanceStatus.Busy);
        });
        received[0].CommittedStatus.Should().Be(WorkflowInstanceStatus.Active);
        received[3].CommittedState.Should().Be("approved");
        received[3].CommittedStatus.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Cancelled_and_faulted_events_are_raised()
    {
        await using var host = await Host(TestWorkflows.Pipeline, TestWorkflows.Errors);
        var cancelled = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        await host.TransitionAsync(cancelled.Id, "cancel");
        var faulted = await host.StartInstanceAsync("errors");
        await host.TransitionAsync(faulted.Id, "fault");

        host.Events.TypesFor(cancelled.Id).Should().EndWith(WorkflowEventType.Cancelled);
        host.Events.TypesFor(faulted.Id).Should().EndWith(WorkflowEventType.Faulted);
        host.Events.Received.Last(e => e.Event.InstanceId == faulted.Id).CommittedStatus.Should().Be(WorkflowInstanceStatus.Faulted);
    }

    [Fact]
    public async Task Failing_event_handler_does_not_break_the_transition()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        host.Events.Throw = true;

        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        var moved = await host.TransitionAsync(instance.Id, "submit");

        moved.State.Should().Be("review");
        moved.Status.Should().Be(WorkflowInstanceStatus.Active);
        host.Events.TypesFor(instance.Id).Should().Contain(WorkflowEventType.StateChanged);
    }

    [Fact]
    public async Task No_events_are_raised_for_rejected_commands()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });
        var before = host.Events.Received.Count;

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "approve")).Should().ThrowAsync<WorkflowForbiddenException>();

        host.Events.Received.Should().HaveCount(before);
    }

    // ───────────── list ─────────────

    [Fact]
    public async Task List_filters_by_state_status_and_key_and_pages()
    {
        await using var host = await Host(TestWorkflows.Pipeline);
        for (var i = 0; i < 5; i++)
        {
            var instance = await host.StartInstanceAsync("pipeline", new { name = $"item{i}" }, key: $"k{i}");
            host.Time.Advance(TimeSpan.FromSeconds(1));
            if (i % 2 == 0)
            {
                await host.TransitionAsync(instance.Id, "submit");
            }
        }

        async Task<(List<WorkflowInstanceModel> Items, int Count)> List(string? state = null, WorkflowInstanceStatus? status = null, string? key = null, int page = 1, int size = 20) =>
            await host.WithEngineAsync(e => e.ListAsync("pipeline", WorkflowUser.System, state, status, key, page, size, TestContext.Current.CancellationToken));

        (await List()).Count.Should().Be(5);
        (await List(state: "review")).Count.Should().Be(3);
        (await List(state: "draft")).Items.Select(i => i.Key).Should().BeEquivalentTo("k1", "k3");
        (await List(status: WorkflowInstanceStatus.Completed)).Count.Should().Be(0);
        (await List(key: "k2")).Items.Should().ContainSingle().Which.State.Should().Be("review");

        var page = await List(page: 2, size: 2);
        page.Count.Should().Be(5);
        page.Items.Select(i => i.Key).Should().Equal("k2", "k1"); // newest first
    }
}

/// <summary>Runs a callback once, right before the first workflow instance with a business key is inserted.</summary>
internal sealed class RaceInterceptor : SaveChangesInterceptor
{
    private int fired;

    public Func<Task>? OnFirstInsert { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (OnFirstInsert is not null &&
            eventData.Context!.ChangeTracker.Entries<WorkflowInstance>().Any(e => e.State == EntityState.Added && e.Entity.BusinessKey is not null) &&
            Interlocked.Exchange(ref fired, 1) == 0)
        {
            await OnFirstInsert();
        }

        return result;
    }
}
