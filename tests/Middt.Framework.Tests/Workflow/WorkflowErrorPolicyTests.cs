using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowErrorPolicyTests
{
    private static async Task<(WorkflowTestHost Host, Guid Id)> StartErrorsAsync(Action<Middt.Framework.Workflow.MiddtWorkflowOptions>? configure = null)
    {
        var host = await WorkflowTestHost.StartAsync([TestWorkflows.Errors], configure);
        var instance = await host.StartInstanceAsync("errors");
        return (host, instance.Id);
    }

    private static FlakyCounter Counter(WorkflowTestHost host) => host.Services.GetRequiredService<FlakyCounter>();

    [Fact]
    public async Task Retry_policy_reruns_the_task_until_it_succeeds()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "retry-ok");

        result.Status.Should().Be(WorkflowInstanceStatus.Completed);
        result.Data["flakyCalls"]!.GetValue<int>().Should().Be(3);
        Counter(host).Calls("flaky-3").Should().Be(3);
        (await host.IncidentsAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Exhausted_retries_fault_the_instance_with_the_attempt_count()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "retry-exhausted");

        result.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        result.State.Should().Be("idle");
        result.Error.Should().Be("flaky failure 2");
        Counter(host).Calls("flaky-5").Should().Be(2);
        var incident = (await host.IncidentsAsync(id)).Should().ContainSingle().Subject;
        incident.Attempts.Should().Be(2);
        incident.TaskKey.Should().Be("flaky-5");
        incident.TransitionKey.Should().Be("retry-exhausted");
        incident.State.Should().Be("idle");
        incident.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Retry_backoff_is_capped_by_MaxRetryDelay()
    {
        // The policy asks for 10 and 20 minutes between attempts; MaxRetryDelay caps each wait at 2 seconds.
        var (host, id) = await StartErrorsAsync(o => o.MaxRetryDelay = TimeSpan.FromSeconds(2));
        await using var _ = host;

        var transition = host.TransitionAsync(id, "backoff");
        var advanced = TimeSpan.Zero;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!transition.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromMilliseconds(500));
            advanced += TimeSpan.FromMilliseconds(500);
        }

        var result = await transition;
        result.Status.Should().Be(WorkflowInstanceStatus.Completed);
        Counter(host).Calls("flaky-backoff").Should().Be(3);
        advanced.Should().BeLessThan(TimeSpan.FromMinutes(1), "two capped waits of 2s are needed instead of 30 minutes");
        advanced.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Workflow_level_policy_applies_to_tasks_without_their_own()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "wf-policy", "onError": { "maxAttempts": 4, "delay": "00:00:00" },
              "start": { "key": "start", "target": "a", "onExecute": [ { "task": "flaky", "key": "f", "config": { "failTimes": 3 } } ] },
              "states": [ { "key": "a" } ] }
            """);

        var instance = await host.StartInstanceAsync("wf-policy");

        instance.Status.Should().Be(WorkflowInstanceStatus.Active);
        instance.State.Should().Be("a");
        Counter(host).Calls("f").Should().Be(4);
    }

    [Fact]
    public async Task Continue_policy_records_an_incident_and_continues()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "continue");

        result.Status.Should().Be(WorkflowInstanceStatus.Completed);
        result.State.Should().Be("done");
        host.Log.Entries.Should().Contain("after-optional");
        var incident = (await host.IncidentsAsync(id)).Should().ContainSingle().Subject;
        incident.TaskKey.Should().Be("optional");
        incident.Error.Should().Be("optional failed");
        incident.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Transition_policy_rolls_back_the_hop_and_runs_the_error_transition()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "redirect", new { x = 1 });

        result.Status.Should().Be(WorkflowInstanceStatus.Active);
        result.State.Should().Be("error-state");
        result.Data.ContainsKey("beforeFailure").Should().BeFalse("data written by the failed hop is rolled back");
        result.Data.ContainsKey("dirty").Should().BeFalse();
        result.Data.ContainsKey("redirectInput").Should().BeFalse();
        host.Log.Entries.Should().Contain("error-state.entry");
        (await host.IncidentsAsync(id)).Should().ContainSingle().Which.TaskKey.Should().Be("remote");

        var history = await host.HistoryAsync(id);
        history.Last().Should().Match<WorkflowHistoryModel>(h => h.TransitionKey == "to-error" && h.Trigger == "Error" && h.FromState == "idle");
        history.Should().NotContain(h => h.TransitionKey == "redirect");
    }

    [Fact]
    public async Task Fault_policy_keeps_state_and_data_and_records_the_pending_transition()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "fault", new { attempt = 1 });

        result.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        result.State.Should().Be("idle");
        result.Data.ContainsKey("faultInput").Should().BeFalse();
        result.AvailableTransitions.Should().BeEmpty();
        var row = await host.RowAsync(id);
        row.PendingTransition.Should().Be("fault");
        row.PendingInput.Should().Be("""{"attempt":1}""");
        row.BusySince.Should().BeNull();
        (await host.IncidentsAsync(id)).Should().ContainSingle().Which.Error.Should().Be("flaky failure 1");
    }

    [Fact]
    public async Task Failure_in_on_entry_rolls_back_to_the_source_state()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "fault-entry");

        result.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        result.State.Should().Be("idle");
        result.Data.ContainsKey("entered").Should().BeFalse();
        (await host.RowAsync(id)).PendingTransition.Should().Be("fault-entry");
    }

    [Fact]
    public async Task Faulted_instance_rejects_manual_transitions()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "fault");

        await FluentActions.Awaiting(() => host.TransitionAsync(id, "to-error")).Should().ThrowAsync<WorkflowConflictException>().WithMessage("*Faulted*");
    }

    [Fact]
    public async Task Retry_reruns_the_failed_transition_with_its_original_input_and_resolves_incidents()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "fault", new { attempt = 7 });
        host.Time.Advance(TimeSpan.FromMinutes(1));

        var result = await host.RetryAsync(id);

        result.Status.Should().Be(WorkflowInstanceStatus.Completed);
        result.Error.Should().BeNull();
        result.Data["faultInput"]!["attempt"]!.GetValue<int>().Should().Be(7);
        var incident = (await host.IncidentsAsync(id)).Should().ContainSingle().Subject;
        incident.ResolvedAt.Should().Be(WorkflowTestHost.StartTime.UtcDateTime.AddMinutes(1));
        var row = await host.RowAsync(id);
        row.PendingTransition.Should().BeNull();
        row.PendingInput.Should().BeNull();
        (await host.HistoryAsync(id)).Last().Trigger.Should().Be("Retry");
    }

    [Fact]
    public async Task Retry_that_fails_again_stays_faulted_with_a_new_incident()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;
        await host.TransitionAsync(id, "retry-exhausted");

        var result = await host.RetryAsync(id);

        result.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        (await host.IncidentsAsync(id)).Should().HaveCount(2);
        (await host.RowAsync(id)).PendingTransition.Should().Be("retry-exhausted");
    }

    [Fact]
    public async Task Retry_of_a_faulted_start_reruns_the_start_transition()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "start-fault",
              "start": { "key": "start", "target": "a",
                         "input": { "schema": { "type": "object" }, "into": "request" },
                         "onExecute": [ { "task": "flaky", "key": "s", "config": { "failTimes": 1 } } ] },
              "states": [ { "key": "a" } ] }
            """);

        var instance = await host.StartInstanceAsync("start-fault", new { v = 1 });
        instance.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        instance.State.Should().BeEmpty();

        var retried = await host.RetryAsync(instance.Id);
        retried.Status.Should().Be(WorkflowInstanceStatus.Active);
        retried.State.Should().Be("a");
        retried.Data["request"]!["v"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task Retry_of_an_instance_that_is_not_faulted_is_a_conflict()
    {
        var (host, id) = await StartErrorsAsync();
        await using var _ = host;

        await FluentActions.Awaiting(() => host.RetryAsync(id)).Should().ThrowAsync<WorkflowConflictException>().WithMessage("Only faulted*");
    }

    [Fact]
    public async Task Broken_task_condition_faults_and_rolls_back_the_transition()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "runtime-error",
              "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object" } } },
              "states": [
                { "key": "a", "transitions": [ { "key": "go", "target": "b" } ] },
                { "key": "b", "onEntry": [ { "task": "record", "when": "data.text > 5", "config": { "name": "never" } } ] } ] }
            """);
        var instance = await host.StartInstanceAsync("runtime-error", new { text = "abc" });

        // A runtime error in a task's "when" condition goes through the error policy (default: Fault), not a 500.
        await host.TransitionAsync(instance.Id, "go");

        var row = await host.RowAsync(instance.Id);
        row.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        row.BusySince.Should().BeNull();
        row.State.Should().Be("a");
        row.PendingTransition.Should().Be("go");
        row.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Broken_task_condition_during_start_faults_with_a_retryable_start()
    {
        await using var host = await WorkflowTestHost.StartAsync("""
            { "key": "start-runtime-error",
              "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object" } } },
              "states": [ { "key": "a", "onEntry": [ { "task": "record", "when": "data.text > 5", "config": { "name": "never" } } ] } ] }
            """);

        await host.StartInstanceAsync("start-runtime-error", new { text = "abc" });

        var row = await host.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Instances, TestContext.Current.CancellationToken));
        row.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        row.BusySince.Should().BeNull();
        row.Error.Should().NotBeNullOrEmpty();
        row.PendingTransition.Should().Be("start");
    }

    [Fact]
    public async Task Unexpected_persistence_failure_faults_with_a_retryable_transition()
    {
        var failOnce = new FailOnceOnHistoryInterceptor();
        await using var host = await WorkflowTestHost.StartAsync(["""
            { "key": "save-failure",
              "start": { "key": "start", "target": "a" },
              "states": [
                { "key": "a", "transitions": [ { "key": "go", "target": "b", "input": { "schema": { "type": "object" } } } ] },
                { "key": "b", "type": "Finish" } ] }
            """], configureDb: db => db.AddInterceptors(failOnce));
        var instance = await host.StartInstanceAsync("save-failure");
        failOnce.Armed = true;

        await FluentActions.Awaiting(() => host.TransitionAsync(instance.Id, "go", new { note = "x" })).Should().ThrowAsync<InvalidOperationException>();

        var row = await host.RowAsync(instance.Id);
        row.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        row.BusySince.Should().BeNull();
        row.State.Should().Be("a");
        row.PendingTransition.Should().Be("go");
        row.PendingInput.Should().Contain("\"note\":\"x\"");
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle(i => i.TransitionKey == "go" && i.ResolvedAt == null);

        var retried = await host.RetryAsync(instance.Id);

        retried.Status.Should().Be(WorkflowInstanceStatus.Completed);
        retried.Data["note"]!.GetValue<string>().Should().Be("x");
        (await host.IncidentsAsync(instance.Id)).Should().OnlyContain(i => i.ResolvedAt != null);
    }
}

/// <summary>Throws once while a history row is being saved (simulates a database failure in the middle of a command).</summary>
internal sealed class FailOnceOnHistoryInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Armed && eventData.Context!.ChangeTracker.Entries<Middt.Framework.Workflow.Persistence.WorkflowHistory>()
                .Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added))
        {
            Armed = false;
            throw new InvalidOperationException("simulated database failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
