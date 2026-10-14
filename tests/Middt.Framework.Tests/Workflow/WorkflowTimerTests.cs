using Microsoft.EntityFrameworkCore;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowTimerTests
{
    private static DateTime T0 => WorkflowTestHost.StartTime.UtcDateTime;

    private static async Task<(WorkflowTestHost Host, Guid Id)> StartTimersAsync()
    {
        var host = await WorkflowTestHost.StartAsync(TestWorkflows.Timers);
        var instance = await host.StartInstanceAsync("timers");
        return (host, instance.Id);
    }

    [Fact]
    public async Task Entering_a_state_arms_its_scheduled_transitions_and_the_workflow_timeout()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;

        var instance = await host.GetAsync(id);
        instance.ScheduledTransitions.Should().ContainSingle().Which.Should().Match<ScheduledTransitionModel>(s => s.Key == "escalate" && s.DueAt == T0.AddMinutes(10));
        instance.AvailableTransitions.Select(t => t.Key).Should().NotContain("escalate");

        var jobs = await host.JobsAsync(id);
        jobs.Select(j => j.Kind).Should().BeEquivalentTo([WorkflowJobKind.Scheduled, WorkflowJobKind.Timeout]);
        jobs.Single(j => j.Kind == WorkflowJobKind.Timeout).DueAt.Should().Be(T0.AddDays(1));
    }

    [Fact]
    public async Task Scheduled_transition_fires_only_after_its_delay()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;

        (await host.RunJobsAsync(TimeSpan.FromMinutes(9))).Should().Be(0);
        (await host.GetAsync(id)).State.Should().Be("waiting");

        (await host.RunJobsAsync(TimeSpan.FromMinutes(1))).Should().Be(1);
        var instance = await host.GetAsync(id);
        instance.State.Should().Be("escalated");
        instance.ScheduledTransitions.Should().BeEmpty();
        host.Log.Entries.Should().Equal("escalated.entry");

        var last = (await host.HistoryAsync(id)).Last();
        last.Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "escalate" && h.Trigger == "Scheduled" && h.ExecutedBy == "system");
        (await host.JobsAsync(id)).Should().ContainSingle().Which.Kind.Should().Be(WorkflowJobKind.Timeout);
    }

    [Fact]
    public async Task Leaving_the_state_cancels_its_scheduled_transition_and_reentering_rearms_it()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;

        await host.TransitionAsync(id, "proceed");
        (await host.JobsAsync(id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Scheduled);

        await host.RunJobsAsync(TimeSpan.FromMinutes(20));
        (await host.GetAsync(id)).State.Should().Be("working");

        var back = await host.TransitionAsync(id, "back");
        back.ScheduledTransitions.Should().ContainSingle().Which.DueAt.Should().Be(T0.AddMinutes(30));
    }

    [Fact]
    public async Task Stale_scheduled_job_for_a_state_that_was_left_is_ignored_and_removed()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "proceed");
        await host.WithDbAsync(async db =>
        {
            db.Jobs.Add(new WorkflowJob
            {
                Id = Guid.NewGuid(), InstanceId = id, Kind = WorkflowJobKind.Scheduled, TransitionKey = "escalate", State = "waiting",
                DueAt = T0, CreatedAt = T0,
            });
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        (await host.RunJobsAsync()).Should().Be(1);

        (await host.GetAsync(id)).State.Should().Be("working");
        (await host.JobsAsync(id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Scheduled);
    }

    [Fact]
    public async Task Workflow_timeout_moves_the_instance_to_the_timeout_target()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "proceed");

        await host.RunJobsAsync(TimeSpan.FromDays(1));

        var instance = await host.GetAsync(id);
        instance.State.Should().Be("expired");
        instance.StateSubType.Should().Be("Timeout");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await host.HistoryAsync(id)).Last().Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "$timeout" && h.Trigger == "Timeout");
        (await host.JobsAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Finishing_the_instance_removes_all_its_jobs()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;

        await host.TransitionAsync(id, "finish");

        (await host.JobsAsync(id)).Should().BeEmpty();
        await host.RunJobsAsync(TimeSpan.FromDays(2));
        (await host.GetAsync(id)).State.Should().Be("done");
    }

    [Fact]
    public async Task Async_transition_is_queued_and_executed_by_the_processor()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;

        var queued = await host.TransitionAsync(id, "proceed", user: WorkflowTestHost.User("bob"), runAsync: true);

        queued.State.Should().Be("waiting");
        var job = (await host.JobsAsync(id)).Should().ContainSingle(j => j.Kind == WorkflowJobKind.Transition).Subject;
        job.RequestedBy.Should().Be("bob");
        job.State.Should().Be("waiting");

        await host.RunJobsAsync();

        (await host.GetAsync(id)).State.Should().Be("working");
        (await host.HistoryAsync(id)).Last().Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "proceed" && h.ExecutedBy == "bob" && h.Trigger == "Manual");
        (await host.JobsAsync(id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Transition);
    }

    [Fact]
    public async Task Async_transition_validates_roles_and_input_before_queueing()
    {
        await using var host = await WorkflowTestHost.StartAsync(TestWorkflows.Pipeline);
        var instance = await host.StartInstanceAsync("pipeline", new { name = "widget" });

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "approve", runAsync: true)).Should().ThrowAsync<WorkflowForbiddenException>();
        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "submit", new { comment = 5 }, runAsync: true)).Should().ThrowAsync<WorkflowValidationException>();
        (await host.JobsAsync(instance.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Async_transition_on_a_busy_instance_is_rejected()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.SetStatusAsync(id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await FluentActions.Awaiting(() => host.TransitionAsync(id, "proceed", runAsync: true)).Should().ThrowAsync<WorkflowConflictException>();
    }

    [Fact]
    public async Task Queued_job_for_a_busy_instance_is_retried_later()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "proceed", runAsync: true);
        await host.SetStatusAsync(id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.RunJobsAsync();

        var job = (await host.JobsAsync(id)).Single(j => j.Kind == WorkflowJobKind.Transition);
        job.Attempts.Should().Be(1);
        job.LockedUntil.Should().BeNull();
        job.DueAt.Should().BeAfter(host.Time.GetUtcNow().UtcDateTime);
        (await host.RowAsync(id)).State.Should().Be("waiting");

        await host.SetStatusAsync(id, WorkflowInstanceStatus.Active);
        await host.RunJobsAsync(TimeSpan.FromSeconds(1));

        (await host.GetAsync(id)).State.Should().Be("working");
        (await host.JobsAsync(id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Transition);
    }

    [Fact]
    public async Task Scheduled_job_for_a_busy_instance_is_retried_later()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        host.Time.Advance(TimeSpan.FromMinutes(10));
        await host.SetStatusAsync(id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.RunJobsAsync();
        (await host.JobsAsync(id)).Should().Contain(j => j.Kind == WorkflowJobKind.Scheduled);

        await host.SetStatusAsync(id, WorkflowInstanceStatus.Active);
        await host.RunJobsAsync(TimeSpan.FromSeconds(1));
        (await host.GetAsync(id)).State.Should().Be("escalated");
    }

    [Fact]
    public async Task Queued_transition_that_is_no_longer_available_is_dropped()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "proceed", runAsync: true);
        await host.TransitionAsync(id, "proceed"); // executed synchronously first: "proceed" no longer exists in "working"

        await host.RunJobsAsync();

        var row = await host.RowAsync(id);
        row.State.Should().Be("working");
        row.Status.Should().Be(WorkflowInstanceStatus.Active);
        (await host.JobsAsync(id)).Should().NotContain(j => j.Kind == WorkflowJobKind.Transition);
    }

    [Fact]
    public async Task Instance_stuck_busy_longer_than_BusyTimeout_is_marked_faulted()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.SetStatusAsync(id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.RunJobsAsync(TimeSpan.FromMinutes(4));
        (await host.RowAsync(id)).Status.Should().Be(WorkflowInstanceStatus.Busy);

        await host.RunJobsAsync(TimeSpan.FromMinutes(2));
        var row = await host.RowAsync(id);
        row.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        row.BusySince.Should().BeNull();
        row.Error.Should().Contain("interrupted");
    }

    [Fact]
    public async Task Jobs_of_finished_instances_are_discarded()
    {
        var (host, id) = await StartTimersAsync();
        await using var _ = host;
        await host.WithDbAsync(db => db.Instances.Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, WorkflowInstanceStatus.Completed), TestContext.Current.CancellationToken));

        (await host.RunJobsAsync(TimeSpan.FromDays(2))).Should().Be(2);

        (await host.JobsAsync(id)).Should().BeEmpty();
        (await host.RowAsync(id)).State.Should().Be("waiting");
    }
}
