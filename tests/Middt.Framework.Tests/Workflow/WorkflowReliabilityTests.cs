using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowReliabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_applies_each_instances_definition_version_access_rule()
    {
        const string v1 = """
            { "key": "versioned-access", "version": 1, "instanceAccess": { "mode": "owner" },
              "start": { "key": "start", "target": "open" }, "states": [ { "key": "open" } ] }
            """;
        const string v2 = """
            { "key": "versioned-access", "version": 2,
              "start": { "key": "start", "target": "open" }, "states": [ { "key": "open" } ] }
            """;
        await using var host = await WorkflowTestHost.StartAsync(v1, v2);
        var oldAlice = await host.StartInstanceAsync("versioned-access", user: WorkflowTestHost.User("alice"));
        var oldBob = await host.StartInstanceAsync("versioned-access", user: WorkflowTestHost.User("bob"));
        var currentBob = await host.StartInstanceAsync("versioned-access", user: WorkflowTestHost.User("bob"));
        await host.WithDbAsync(db => db.Instances.Where(i => i.Id == oldAlice.Id || i.Id == oldBob.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.WorkflowVersion, 1), Ct));

        var list = await host.WithEngineAsync(e => e.ListAsync("versioned-access", WorkflowTestHost.User("alice"), null, null, null, 1, 20, Ct));

        list.Count.Should().Be(2);
        list.Items.Select(i => i.Id).Should().BeEquivalentTo([oldAlice.Id, currentBob.Id]);
    }

    [Fact]
    public async Task Busy_child_is_cancelled_by_durable_job_after_its_claim_ends()
    {
        await using var host = await WorkflowTestHost.StartAsync(TestWorkflows.ParentAsync, TestWorkflows.ChildManual);
        var parent = await host.StartInstanceAsync("parent-async");
        var childId = parent.WaitingForInstanceId!.Value;
        var original = await host.RowAsync(childId);
        await host.SetStatusAsync(childId, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.TransitionAsync(parent.Id, "cancel");

        (await host.RowAsync(childId)).Status.Should().Be(WorkflowInstanceStatus.Busy);
        (await host.JobsAsync(childId)).Should().ContainSingle(j => j.Kind == WorkflowJobKind.CancelChild);
        await host.SetStatusAsync(childId, WorkflowInstanceStatus.Faulted);
        await host.RunJobsAsync(TimeSpan.FromSeconds(1));

        var cancelled = await host.RowAsync(childId);
        cancelled.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        cancelled.Revision.Should().Be(original.Revision + 1);
        (await host.JobsAsync(childId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Recovered_busy_instance_can_retry_its_saved_command()
    {
        const string definition = """
            { "key": "recoverable", "start": { "key": "start", "target": "open" },
              "states": [ { "key": "open", "transitions": [ { "key": "finish", "target": "done" } ] },
                          { "key": "done", "type": "Finish" } ] }
            """;
        await using var host = await WorkflowTestHost.StartAsync([definition]);
        var instance = await host.StartInstanceAsync("recoverable");
        await host.WithDbAsync(db => db.Instances.Where(i => i.Id == instance.Id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, WorkflowInstanceStatus.Busy)
            .SetProperty(i => i.BusySince, host.Time.GetUtcNow().UtcDateTime.AddHours(-1))
            .SetProperty(i => i.PendingTransition, "finish"), Ct));

        await host.Processor.RunOnceAsync(Ct);
        (await host.RowAsync(instance.Id)).Status.Should().Be(WorkflowInstanceStatus.Faulted);
        (await host.IncidentsAsync(instance.Id)).Should().ContainSingle(i => i.TransitionKey == "finish");

        var result = await host.RetryAsync(instance.Id);
        result.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await host.RowAsync(instance.Id)).PendingTransition.Should().BeNull();
    }

    [Fact]
    public async Task Multi_hop_automatic_transitions_increment_data_version_per_hop()
    {
        const string definition = """
            {
              "key": "multi-hop",
              "start": { "key": "start", "target": "step1", "onExecute": [ { "task": "set", "config": { "values": { "a": "1" } } } ] },
              "states": [
                {
                  "key": "step1",
                  "transitions": [
                    { "key": "to-step2", "target": "step2", "trigger": "Automatic", "default": true, "onExecute": [ { "task": "set", "config": { "values": { "b": "2" } } } ] }
                  ]
                },
                {
                  "key": "step2",
                  "transitions": [
                    { "key": "to-done", "target": "done", "trigger": "Automatic", "default": true, "onExecute": [ { "task": "set", "config": { "values": { "c": "3" } } } ] }
                  ]
                },
                { "key": "done", "type": "Finish" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync([definition]);
        var instance = await host.StartInstanceAsync("multi-hop");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.DataVersion.Should().Be(3);
        var history = await host.HistoryAsync(instance.Id);
        history.Select(h => (h.TransitionKey, h.DataVersion)).Should().Equal(
            ("start", 1),
            ("to-step2", 2),
            ("to-done", 3));
    }

    [Fact]
    public async Task Failed_task_attempt_does_not_leak_data_into_the_next_attempt()
    {
        const string definition = """
            { "key": "attempt-isolation", "start": { "key": "start", "target": "done",
                "onExecute": [ { "task": "mutate-once", "onError": { "maxAttempts": 2, "delay": "00:00:00" } } ] },
              "states": [ { "key": "done", "type": "Finish" } ] }
            """;
        await using var host = await WorkflowTestHost.StartAsync([definition],
            configure: o => o.AddTask<MutateOnceTask>("mutate-once"),
            services: s => s.AddSingleton<AttemptProbe>());

        var instance = await host.StartInstanceAsync("attempt-isolation");
        var probe = host.Services.GetRequiredService<AttemptProbe>();

        instance.Data["successful"].Should().NotBeNull();
        instance.Data.ContainsKey("dirty").Should().BeFalse();
        probe.SawDirtyOnRetry.Should().BeFalse();
        probe.Keys.Should().HaveCount(2).And.OnlyContain(k => k == probe.Keys[0]);
    }

    [Fact]
    public async Task Failed_event_delivery_remains_queued_and_is_replayed()
    {
        const string definition = """
            { "key": "event-replay", "start": { "key": "start", "target": "open" },
              "states": [ { "key": "open" } ] }
            """;
        await using var host = await WorkflowTestHost.StartAsync([definition],
            services: s =>
            {
                s.AddSingleton<FailFirstEventHandler>();
                s.AddSingleton<IWorkflowEventHandler>(p => p.GetRequiredService<FailFirstEventHandler>());
            });
        var instance = await host.StartInstanceAsync("event-replay");
        var handler = host.Services.GetRequiredService<FailFirstEventHandler>();

        (await host.JobsAsync(instance.Id)).Should().ContainSingle(j => j.Kind == WorkflowJobKind.Event);
        await host.RunJobsAsync(TimeSpan.FromSeconds(6));

        handler.Calls.Should().BeGreaterThan(1);
        (await host.JobsAsync(instance.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Finishing_an_instance_does_not_delete_undelivered_events()
    {
        const string definition = """
            { "key": "event-retention", "start": { "key": "start", "target": "open" },
              "states": [ { "key": "open", "transitions": [ { "key": "finish", "target": "done" } ] },
                          { "key": "done", "type": "Finish" } ] }
            """;
        await using var host = await WorkflowTestHost.StartAsync([definition],
            services: s => s.AddSingleton<IWorkflowEventHandler, AlwaysFailEventHandler>());
        var instance = await host.StartInstanceAsync("event-retention");
        var pending = (await host.JobsAsync(instance.Id)).Where(j => j.Kind == WorkflowJobKind.Event).Select(j => j.Id).ToArray();
        pending.Should().NotBeEmpty();

        await host.TransitionAsync(instance.Id, "finish");

        (await host.JobsAsync(instance.Id)).Select(j => j.Id).Should().Contain(pending);
    }

    public sealed class AttemptProbe
    {
        public int Calls { get; set; }
        public bool SawDirtyOnRetry { get; set; }
        public List<string> Keys { get; } = [];
    }

    public sealed class MutateOnceTask(AttemptProbe probe) : IWorkflowTask
    {
        public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
        {
            probe.Calls++;
            probe.Keys.Add(context.IdempotencyKey);
            if (probe.Calls == 1)
            {
                context.Data["dirty"] = true;
                throw new InvalidOperationException("transient");
            }

            probe.SawDirtyOnRetry = context.Data.ContainsKey("dirty") || context.Scope.Data.ContainsKey("dirty");
            context.Data["successful"] = true;
            return Task.CompletedTask;
        }
    }

    public sealed class FailFirstEventHandler : IWorkflowEventHandler
    {
        public int Calls { get; private set; }

        public Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                throw new InvalidOperationException("delivery failed");
            }

            return Task.CompletedTask;
        }
    }

    public sealed class AlwaysFailEventHandler : IWorkflowEventHandler
    {
        public Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("delivery failed");
    }
}
