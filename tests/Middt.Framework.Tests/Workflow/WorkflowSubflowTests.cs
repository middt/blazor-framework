using Microsoft.EntityFrameworkCore;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowSubflowTests
{
    private static Task<WorkflowTestHost> Host() =>
        WorkflowTestHost.StartAsync(TestWorkflows.ParentSync, TestWorkflows.ParentAsync, TestWorkflows.ChildAuto, TestWorkflows.ChildManual, TestWorkflows.Launcher);

    private static Task<List<WorkflowInstance>> ChildrenAsync(WorkflowTestHost host, Guid parentId) =>
        host.WithDbAsync(db => db.Instances.AsNoTracking().Where(i => i.ParentInstanceId == parentId).ToListAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task Synchronous_child_completion_continues_the_parent_immediately()
    {
        await using var host = await Host();

        var parent = await host.StartInstanceAsync("parent-sync", new { amount = 100, name = "ann" });

        parent.State.Should().Be("approved");
        parent.Status.Should().Be(WorkflowInstanceStatus.Completed);
        parent.WaitingForInstanceId.Should().BeNull();
        parent.Data["credit"]!["score"]!.GetValue<decimal>().Should().Be(85);
        parent.Data["credit"]!["amount"]!.GetValue<decimal>().Should().Be(100);
        parent.Data["credit"]!.AsObject().ContainsKey("name").Should().BeFalse("only the mapped subFlow.input is passed to the child");

        var child = (await ChildrenAsync(host, parent.Id)).Should().ContainSingle().Subject;
        child.WorkflowKey.Should().Be("child-auto");
        child.State.Should().Be("passed");
        child.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Child_subtype_drives_the_parent_rules()
    {
        await using var host = await Host();

        var parent = await host.StartInstanceAsync("parent-sync", new { amount = 50_000 });

        parent.State.Should().Be("rejected");
        parent.Data["credit"]!["score"]!.GetValue<decimal>().Should().Be(40);
    }

    [Fact]
    public async Task Leftover_resume_job_of_a_synchronous_child_is_harmless()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-sync", new { amount = 100 });

        await host.RunJobsAsync(TimeSpan.FromSeconds(5));

        (await host.GetAsync(parent.Id)).State.Should().Be("approved");
        (await host.JobsAsync(parent.Id)).Should().BeEmpty();
        (await host.HistoryAsync(parent.Id)).Should().NotContain(h => h.TransitionKey == "$subflow-completed");
    }

    [Fact]
    public async Task Parent_waits_for_an_asynchronous_child()
    {
        await using var host = await Host();

        var parent = await host.StartInstanceAsync("parent-async", new { name = "ann" });

        parent.State.Should().Be("waiting-child");
        parent.Status.Should().Be(WorkflowInstanceStatus.Active);
        var child = (await ChildrenAsync(host, parent.Id)).Should().ContainSingle().Subject;
        parent.WaitingForInstanceId.Should().Be(child.Id);
        child.State.Should().Be("pending");
        child.StartedBy.Should().Be("alice", "children run as the system on behalf of the parent's actor");
        JsonNodeString(child.Data, "name").Should().Be("ann", "without subFlow.input the whole parent data is copied");

        parent.AvailableTransitions.Select(t => t.Key).Should().Equal("cancel");
    }

    [Fact]
    public async Task Manual_transitions_of_a_waiting_parent_are_blocked()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-async");

        await FluentActions.Awaiting(() => host.TransitionAsync(parent.Id, "poke")).Should().ThrowAsync<WorkflowConflictException>()
            .WithMessage("*waiting for a subflow*");
    }

    [Fact]
    public async Task Completing_the_child_resumes_the_parent()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-async");
        var childId = parent.WaitingForInstanceId!.Value;

        var child = await host.TransitionAsync(childId, "complete", new { ok = true });

        child.Status.Should().Be(WorkflowInstanceStatus.Completed);
        var resumed = await host.GetAsync(parent.Id);
        resumed.State.Should().Be("approved");
        resumed.Status.Should().Be(WorkflowInstanceStatus.Completed);
        resumed.WaitingForInstanceId.Should().BeNull();
        resumed.Data["answer"]!["ok"]!.GetValue<bool>().Should().BeTrue("without outputInto the child data is merged into the root");

        var history = await host.HistoryAsync(parent.Id);
        history.Select(h => h.TransitionKey).Should().Equal("start", "$subflow-completed", "ok");
        history[1].Trigger.Should().Be("SubFlow");
    }

    [Fact]
    public async Task Child_ending_in_an_error_state_routes_the_parent_to_the_default_branch()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-async");

        await host.TransitionAsync(parent.WaitingForInstanceId!.Value, "decline");

        (await host.GetAsync(parent.Id)).State.Should().Be("rejected");
    }

    [Fact]
    public async Task Parent_busy_when_the_child_completes_is_resumed_by_a_job()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-async");
        var childId = parent.WaitingForInstanceId!.Value;
        await host.SetStatusAsync(parent.Id, WorkflowInstanceStatus.Busy, host.Time.GetUtcNow().UtcDateTime);

        await host.TransitionAsync(childId, "complete");

        (await host.JobsAsync(parent.Id)).Should().ContainSingle(j => j.Kind == WorkflowJobKind.ResumeParent);
        await host.SetStatusAsync(parent.Id, WorkflowInstanceStatus.Active);

        await host.RunJobsAsync(TimeSpan.FromSeconds(2));

        (await host.GetAsync(parent.Id)).State.Should().Be("approved");
        (await host.JobsAsync(parent.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_waiting_parent_also_cancels_the_child()
    {
        await using var host = await Host();
        var parent = await host.StartInstanceAsync("parent-async");
        var childId = parent.WaitingForInstanceId!.Value;

        var cancelled = await host.TransitionAsync(parent.Id, "cancel");

        cancelled.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        cancelled.WaitingForInstanceId.Should().BeNull();
        var child = await host.RowAsync(childId);
        child.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        child.CompletedAt.Should().NotBeNull();

        await FluentActions.Awaiting(() => host.TransitionAsync(childId, "complete")).Should().ThrowAsync<WorkflowConflictException>();
    }

    [Fact]
    public async Task Workflow_timeout_of_a_waiting_parent_cancels_the_child()
    {
        await using var host = await WorkflowTestHost.StartAsync(TestWorkflows.ChildManual, """
            { "key": "parent-timeout", "timeout": { "after": "PT1H", "target": "expired" },
              "start": { "key": "start", "target": "waiting" },
              "states": [
                { "key": "waiting", "type": "SubFlow", "subFlow": { "workflow": "child-manual" },
                  "transitions": [ { "key": "next", "target": "expired", "trigger": "Automatic", "default": true } ] },
                { "key": "expired", "type": "Finish", "subType": "Timeout" } ] }
            """);
        var parent = await host.StartInstanceAsync("parent-timeout");
        var childId = parent.WaitingForInstanceId!.Value;

        await host.RunJobsAsync(TimeSpan.FromHours(1));

        var expired = await host.GetAsync(parent.Id);
        expired.State.Should().Be("expired");
        expired.Status.Should().Be(WorkflowInstanceStatus.Completed);
        expired.WaitingForInstanceId.Should().BeNull();
        (await host.RowAsync(childId)).Status.Should().Be(WorkflowInstanceStatus.Cancelled);
    }

    [Fact]
    public async Task StartWorkflow_task_starts_a_fire_and_forget_child()
    {
        await using var host = await Host();

        var launcher = await host.StartInstanceAsync("launcher", new { orderId = 7 });

        launcher.State.Should().Be("launched");
        launcher.WaitingForInstanceId.Should().BeNull();
        var childId = Guid.Parse(launcher.Data["childId"]!.GetValue<string>());
        var child = await host.RowAsync(childId);
        child.ParentInstanceId.Should().Be(launcher.Id);
        child.BusinessKey.Should().Be("child-7");
        child.WorkflowKey.Should().Be("child-manual");
        JsonNodeString(child.Data, "orderId").Should().Be("7");

        // The parent does not wait: it can move on, and the child finishing later does not touch it.
        (await host.TransitionAsync(launcher.Id, "finish")).State.Should().Be("done");
        await host.TransitionAsync(childId, "complete");
        await host.RunJobsAsync(TimeSpan.FromSeconds(5));
        (await host.GetAsync(launcher.Id)).State.Should().Be("done");
    }

    [Fact]
    public async Task Subflow_can_encapsulate_reusable_database_query_and_return_data_to_parent()
    {
        const string subflowDef = """
            {
              "key": "get-branch-staff",
              "start": { "key": "start", "target": "validate" },
              "states": [
                {
                  "key": "validate",
                  "transitions": [
                    { "key": "valid", "target": "query", "trigger": "Automatic", "rule": "data.branchId != null && data.branchId > 0" },
                    { "key": "invalid", "target": "invalid-param", "trigger": "Automatic", "default": true }
                  ]
                },
                {
                  "key": "query",
                  "onEntry": [
                    {
                      "task": "db-query",
                      "config": {
                        "sql": "SELECT 101 AS id, 'Ali Veli' AS name WHERE 1 = {0}",
                        "parameters": [ "{{ data.branchId }}" ],
                        "into": "items"
                      }
                    }
                  ],
                  "transitions": [
                    { "key": "done", "target": "completed", "trigger": "Automatic", "default": true }
                  ]
                },
                { "key": "completed", "type": "Finish", "subType": "Success" },
                { "key": "invalid-param", "type": "Finish", "subType": "Error" }
              ]
            }
            """;

        const string parentDef = """
            {
              "key": "onboarding-parent",
              "start": { "key": "start", "target": "fetch-staff",
                         "input": { "schema": { "type": "object", "properties": { "branchId": { "type": "integer" } } } } },
              "states": [
                {
                  "key": "fetch-staff",
                  "type": "SubFlow",
                  "subFlow": {
                    "workflow": "get-branch-staff",
                    "input": { "branchId": "data.branchId" },
                    "outputInto": "availableStaff"
                  },
                  "transitions": [
                    { "key": "ok", "target": "assigned", "trigger": "Automatic", "rule": "child.subType == \"Success\"" },
                    { "key": "ko", "target": "failed", "trigger": "Automatic", "default": true }
                  ]
                },
                { "key": "assigned", "type": "Finish", "subType": "Success" },
                { "key": "failed", "type": "Finish", "subType": "Error" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(
            [subflowDef, parentDef],
            configure: options => options.UseApplicationDbContext<WorkflowDbContext>());

        // 1. Success case: valid branchId retrieves data and resumes parent
        var parent = await host.StartInstanceAsync("onboarding-parent", new { branchId = 1 });

        var children = await ChildrenAsync(host, parent.Id);
        children.Should().NotBeEmpty();
        var child = children[0];
        child.State.Should().Be("completed");
        child.Status.Should().Be(WorkflowInstanceStatus.Completed);

        parent.State.Should().Be("assigned");
        parent.Status.Should().Be(WorkflowInstanceStatus.Completed);
        parent.Data["availableStaff"]!["items"]!.AsArray().Should().HaveCount(1);
        parent.Data["availableStaff"]!["items"]![0]!["id"]!.GetValue<long>().Should().Be(101);
        parent.Data["availableStaff"]!["items"]![0]!["name"]!.GetValue<string>().Should().Be("Ali Veli");

        // 2. Failure case: invalid branchId causes child error and parent routes to fallback
        var failedParent = await host.StartInstanceAsync("onboarding-parent", new { branchId = 0 });
        failedParent.State.Should().Be("failed");
        failedParent.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    private static string? JsonNodeString(string json, string property) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)![property]?.ToJsonString().Trim('"');
}
