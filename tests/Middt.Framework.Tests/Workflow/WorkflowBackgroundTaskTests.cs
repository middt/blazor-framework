using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

/// <summary>Controls the <c>gate</c> test task: counts calls, can block until released, can fail.</summary>
public sealed class TaskGate
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public bool Fail { get; set; }

    /// <summary>When set, the task waits for it (a long running call).</summary>
    public TaskCompletionSource? Hold { get; set; }

    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Enter()
    {
        Interlocked.Increment(ref calls);
        Entered.TrySetResult();
    }
}

/// <summary><c>gate</c>: writes <c>data.result</c> (config <c>result</c>, default "ok") – after <see cref="TaskGate.Hold"/>, unless it fails.</summary>
public sealed class GateTask(TaskGate gate) : IWorkflowTask
{
    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        gate.Enter();
        if (gate.Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (gate.Fail)
        {
            throw new WorkflowTaskException("gate failed");
        }

        context.Data["result"] = context.Config["result"]?.GetValue<string>() ?? "ok";
        context.Data["idempotencyKey"] = context.IdempotencyKey;
    }
}

public sealed class WorkflowBackgroundTaskTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Slow = """
        {
          "key": "slow",
          "start": { "key": "start", "target": "working" },
          "states": [
            { "key": "working",
              "onEntry": [
                { "task": "record", "config": { "name": "before" } },
                { "task": "gate", "key": "call", "background": true },
                { "task": "record", "config": { "name": "after" } } ],
              "transitions": [
                { "key": "note", "target": "$self", "input": { "schema": { "type": "object", "properties": { "text": { "type": "string" } } }, "into": "note" } },
                { "key": "abort", "target": "aborted" },
                { "key": "done", "target": "finished", "trigger": "Automatic", "rule": "data.result == \"ok\"" },
                { "key": "rejected", "target": "aborted", "trigger": "Automatic", "rule": "data.result == \"no\"" } ] },
            { "key": "finished", "type": "Finish", "subType": "Success" },
            { "key": "aborted", "type": "Finish", "subType": "Cancelled" }
          ]
        }
        """;

    private static Task<WorkflowTestHost> StartHostAsync(string definition = Slow, TaskGate? gate = null, Action<MiddtWorkflowOptions>? configure = null) =>
        WorkflowTestHost.StartAsync([definition], o =>
        {
            o.AddTask<GateTask>("gate");
            configure?.Invoke(o);
        }, services => services.AddSingleton(gate ?? new TaskGate()));

    [Fact]
    public async Task Entering_the_state_commits_it_and_the_task_runs_in_a_job_outside_the_lock()
    {
        await using var host = await StartHostAsync();
        var gate = host.Services.GetRequiredService<TaskGate>();

        var started = await host.StartInstanceAsync("slow");

        started.Status.Should().Be(WorkflowInstanceStatus.Active, "the instance is not held Busy while the task runs");
        started.State.Should().Be("working");
        gate.Calls.Should().Be(0);
        host.Log.Entries.Should().Equal("before");
        var job = (await host.JobsAsync(started.Id)).Should().ContainSingle().Which;
        job.Kind.Should().Be(WorkflowJobKind.Task);
        (await host.RowAsync(started.Id)).BackgroundTaskId.Should().Be(job.Id);

        await host.RunJobsAsync();

        var instance = await host.GetAsync(started.Id);
        instance.State.Should().Be("finished", "the remaining onEntry tasks and the automatic transitions continue after the task");
        instance.Data["result"]!.GetValue<string>().Should().Be("ok");
        instance.Data["idempotencyKey"]!.GetValue<string>().Should().StartWith(job.Id.ToString("N"), "keys stay stable when the job is retried");
        host.Log.Entries.Should().Equal("before", "after");
        gate.Calls.Should().Be(1);
        (await host.HistoryAsync(started.Id)).Select(h => (h.TransitionKey, h.Trigger)).Should().Equal(
            ("start", "Start"), ("$background", "Background"), ("done", "Automatic"));
        (await host.RowAsync(started.Id)).BackgroundTaskId.Should().BeNull();
    }

    [Fact]
    public async Task Data_changed_while_the_task_runs_is_kept_and_automatic_transitions_wait_for_the_task()
    {
        await using var host = await StartHostAsync();
        var started = await host.StartInstanceAsync("slow");

        var updated = await host.TransitionAsync(started.Id, "note", new { text = "meanwhile" });
        updated.State.Should().Be("working", "automatic transitions are evaluated only after the background task");

        await host.RunJobsAsync();

        var instance = await host.GetAsync(started.Id);
        instance.State.Should().Be("finished");
        instance.Data["note"]!["text"]!.GetValue<string>().Should().Be("meanwhile");
        instance.Data["result"]!.GetValue<string>().Should().Be("ok");
    }

    [Fact]
    public async Task Leaving_the_state_discards_the_task_result()
    {
        await using var host = await StartHostAsync();
        var started = await host.StartInstanceAsync("slow");

        await host.TransitionAsync(started.Id, "abort");
        await host.RunJobsAsync();

        var instance = await host.GetAsync(started.Id);
        instance.State.Should().Be("aborted");
        instance.Data.ContainsKey("result").Should().BeFalse();
        host.Services.GetRequiredService<TaskGate>().Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_failed_task_faults_the_instance_and_retry_runs_it_again()
    {
        await using var host = await StartHostAsync(gate: new TaskGate { Fail = true });
        var gate = host.Services.GetRequiredService<TaskGate>();
        var started = await host.StartInstanceAsync("slow");

        await host.RunJobsAsync();

        var faulted = await host.GetAsync(started.Id);
        faulted.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        faulted.State.Should().Be("working");
        (await host.RowAsync(started.Id)).PendingTransition.Should().Be("$background");
        (await host.IncidentsAsync(started.Id)).Should().ContainSingle().Which.Error.Should().Contain("gate failed");

        gate.Fail = false;
        var retried = await host.RetryAsync(started.Id);
        retried.Status.Should().Be(WorkflowInstanceStatus.Active);
        await host.RunJobsAsync();

        (await host.GetAsync(started.Id)).State.Should().Be("finished");
        gate.Calls.Should().Be(2);
        (await host.IncidentsAsync(started.Id)).Should().OnlyContain(i => i.ResolvedAt != null);
    }

    [Fact]
    public async Task An_error_transition_policy_is_applied_to_a_failed_background_task()
    {
        var definition = Slow.Replace("""{ "task": "gate", "key": "call", "background": true }""",
            """{ "task": "gate", "key": "call", "background": true, "onError": { "action": "Transition", "transition": "abort" } }""", StringComparison.Ordinal);
        await using var host = await StartHostAsync(definition, new TaskGate { Fail = true });
        var started = await host.StartInstanceAsync("slow");

        await host.RunJobsAsync();

        (await host.GetAsync(started.Id)).State.Should().Be("aborted");
    }

    [Fact]
    public async Task A_result_that_cannot_be_applied_yet_is_kept_so_the_task_is_not_run_twice()
    {
        await using var host = await StartHostAsync();
        var gate = host.Services.GetRequiredService<TaskGate>();
        var started = await host.StartInstanceAsync("slow");
        await host.SetStatusAsync(started.Id, WorkflowInstanceStatus.Busy, WorkflowTestHost.StartTime.UtcDateTime);

        await host.RunJobsAsync();
        gate.Calls.Should().Be(1);
        var job = (await host.JobsAsync(started.Id)).Should().ContainSingle().Which;
        JsonNode.Parse(job.Payload!)!["result"]!["status"]!.GetValue<string>().Should().Be("completed");

        await host.SetStatusAsync(started.Id, WorkflowInstanceStatus.Active);
        await host.RunJobsAsync(TimeSpan.FromSeconds(30));

        (await host.GetAsync(started.Id)).State.Should().Be("finished");
        gate.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("""{ "task": "gate", "background": true }""", "onExit", "only allowed in onEntry")]
    [InlineData("""{ "task": "setKey", "config": { "key": "k" }, "background": true }""", "onEntry", "setKey cannot run in the background")]
    public async Task Invalid_background_tasks_fail_at_load(string task, string hook, string error)
    {
        var definition = $$"""
            { "key": "bad", "start": { "key": "s", "target": "a" },
              "states": [ { "key": "a", "{{hook}}": [ {{task}} ], "transitions": [ { "key": "x", "target": "b" } ] }, { "key": "b", "type": "Finish" } ] }
            """;
        var act = () => StartHostAsync(definition);
        (await act.Should().ThrowAsync<WorkflowDefinitionException>()).Which.Message.Should().Contain(error);
    }
}

/// <summary>A long task inside a claim keeps its instance Busy without being mistaken for a crash.</summary>
public sealed class WorkflowBusyHeartbeatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Blocking = """
        {
          "key": "blocking",
          "start": { "key": "start", "target": "ready" },
          "states": [
            { "key": "ready", "transitions": [ { "key": "go", "target": "done", "onExecute": [ { "task": "gate" } ] } ] },
            { "key": "done", "type": "Finish" }
          ]
        }
        """;

    private static async Task<(bool Faulted, WorkflowInstance Row)> RunLongTransitionAsync(TimeSpan heartbeat)
    {
        var gate = new TaskGate { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var host = await WorkflowTestHost.StartAsync([Blocking], o =>
        {
            o.AddTask<GateTask>("gate");
            o.BusyTimeout = TimeSpan.FromMinutes(5);
            o.BusyHeartbeatInterval = heartbeat;
        }, services => services.AddSingleton(gate));
        var started = await host.StartInstanceAsync("blocking");

        var transition = Task.Run(() => host.TransitionAsync(started.Id, "go"), Ct);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Six minutes pass while the task is still running (one minute at a time, so every heartbeat gets its turn).
        for (var minute = 1; minute <= 6; minute++)
        {
            host.Time.Advance(TimeSpan.FromMinutes(1));
            if (heartbeat > TimeSpan.Zero)
            {
                var expected = host.Time.GetUtcNow().UtcDateTime;
                await WaitUntilAsync(async () => (await host.RowAsync(started.Id)).BusySince == expected);
            }
        }

        await host.Processor.RunOnceAsync(Ct); // Busy recovery scan
        var row = await host.RowAsync(started.Id);
        var faulted = row.Status == WorkflowInstanceStatus.Faulted;

        gate.Hold.TrySetResult();
        try
        {
            await transition;
        }
        catch (Exception ex) when (ex is WorkflowConflictException or Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // without the heartbeat: recovery faulted the instance under the running claim, whose commit then fails
        }

        return (faulted, await host.RowAsync(started.Id));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        throw new TimeoutException("condition not met");
    }

    [Fact]
    public async Task Without_a_heartbeat_a_task_running_longer_than_the_busy_timeout_is_faulted_while_it_still_runs()
    {
        var (faulted, _) = await RunLongTransitionAsync(TimeSpan.Zero);
        faulted.Should().BeTrue("this is the behaviour the heartbeat fixes");
    }

    [Fact]
    public async Task With_the_heartbeat_the_running_claim_is_kept_and_the_transition_completes()
    {
        var (faulted, row) = await RunLongTransitionAsync(TimeSpan.FromMinutes(1));

        faulted.Should().BeFalse();
        row.State.Should().Be("done");
        row.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }
}
