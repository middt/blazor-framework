using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowAdvancedFeaturesTests
{
    private sealed class StatusCodeFlakyTask(FlakyCounter counter) : IWorkflowTask
    {
        public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
        {
            var call = counter.Increment(context.Task.DisplayKey);
            var statusCode = context.Config["statusCode"]?.GetValue<int>() ?? 503;
            var failTimes = context.Config["failTimes"]?.GetValue<int>() ?? 1;
            if (call <= failTimes)
            {
                throw new WorkflowTaskException($"HTTP {statusCode} failure", statusCode: statusCode);
            }

            context.Data["calls"] = call;
            return Task.CompletedTask;
        }
    }

    // ─────────────────── Faz 1: 3-Tier Error Boundary & Smart Retry ───────────────────

    [Fact]
    public async Task State_level_onError_policy_catches_task_failure_and_task_level_overrides_state_level()
    {
        const string def = """
            {
              "key": "state-error-boundary",
              "start": { "key": "start", "target": "step1" },
              "states": [
                {
                  "key": "step1",
                  "onError": { "action": "Transition", "transition": "to-fallback", "maxAttempts": 2, "delay": "00:00:00" },
                  "onEntry": [
                    { "key": "task-inherits-state-policy", "task": "fail" }
                  ],
                  "transitions": [
                    { "key": "to-fallback", "target": "fallback", "trigger": "Manual" }
                  ]
                },
                {
                  "key": "fallback",
                  "onError": { "action": "Fault", "maxAttempts": 1, "delay": "00:00:00" },
                  "onEntry": [
                    {
                      "key": "task-overrides-state-policy",
                      "task": "fail",
                      "onError": { "action": "Continue", "maxAttempts": 1, "delay": "00:00:00" }
                    }
                  ],
                  "transitions": [
                    { "key": "finish", "target": "done", "trigger": "Automatic", "default": true }
                  ]
                },
                { "key": "done", "type": "Finish", "subType": "Success" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var instance = await host.StartInstanceAsync("state-error-boundary");

        instance.State.Should().Be("done");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);

        var incidents = await host.IncidentsAsync(instance.Id);
        incidents.Should().HaveCount(2);
        incidents[0].TaskKey.Should().Be("task-inherits-state-policy");
        incidents[0].Attempts.Should().Be(2);
        incidents[1].TaskKey.Should().Be("task-overrides-state-policy");
        incidents[1].Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RetryOnStatusCodes_retries_transient_5xx_and_fails_fast_on_400()
    {
        const string def = """
            {
              "key": "smart-retry",
              "onError": {
                "action": "Fault",
                "maxAttempts": 3,
                "delay": "00:00:00",
                "backoffMultiplier": 1.5,
                "maxDelay": "PT5S",
                "useJitter": true,
                "retryOnStatusCodes": ["5xx", "!400"]
              },
              "start": { "key": "start", "target": "waiting" },
              "states": [
                {
                  "key": "waiting",
                  "transitions": [
                    {
                      "key": "call-503",
                      "target": "done",
                      "onExecute": [
                        { "key": "transient-503", "task": "status-flaky", "config": { "statusCode": 503, "failTimes": 2 } }
                      ]
                    },
                    {
                      "key": "call-400",
                      "target": "done",
                      "onExecute": [
                        { "key": "bad-request-400", "task": "status-flaky", "config": { "statusCode": 400, "failTimes": 2 } }
                      ]
                    }
                  ]
                },
                { "key": "done", "type": "Finish", "subType": "Success" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(
            [def],
            configure: o => o.AddTask<StatusCodeFlakyTask>("status-flaky"));

        // 1. 503 matches "5xx" -> retries 2 failures and succeeds on attempt 3
        var inst1 = await host.StartInstanceAsync("smart-retry");
        inst1 = await host.TransitionAsync(inst1.Id, "call-503");
        inst1.State.Should().Be("done");
        inst1.Data["calls"]!.GetValue<int>().Should().Be(3);

        // 2. 400 matches "!400" -> does NOT retry, faults immediately after 1 attempt
        var inst2 = await host.StartInstanceAsync("smart-retry");
        inst2 = await host.TransitionAsync(inst2.Id, "call-400");
        inst2.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        var incidents = await host.IncidentsAsync(inst2.Id);
        incidents.Should().ContainSingle().Which.Attempts.Should().Be(1);
    }

    // ─────────────────── Faz 1: $self Reenter & Automatic Triggering + Dynamic Timers ───────────────────

    [Fact]
    public async Task Self_transition_evaluates_automatic_transitions_and_reenter_runs_exit_entry_hooks()
    {
        const string def = """
            {
              "key": "self-semantics",
              "start": {
                "key": "start",
                "target": "collecting",
                "input": { "set": { "total": "0" } }
              },
              "states": [
                {
                  "key": "collecting",
                  "onEntry": [ { "task": "record", "config": { "name": "collecting.entry" } } ],
                  "onExit": [ { "task": "record", "config": { "name": "collecting.exit" } } ],
                  "transitions": [
                    {
                      "key": "add",
                      "target": "$self",
                      "input": {
                        "schema": { "type": "object", "properties": { "delta": { "type": "number" } } },
                        "set": { "total": "data.total + input.delta" }
                      }
                    },
                    {
                      "key": "refresh",
                      "target": "$self",
                      "reenter": true,
                      "onExecute": [ { "task": "record", "config": { "name": "refresh.execute" } } ]
                    },
                    {
                      "key": "auto-complete",
                      "target": "done",
                      "trigger": "Automatic",
                      "rule": "data.total >= 100"
                    }
                  ]
                },
                { "key": "done", "type": "Finish", "subType": "Success" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var inst = await host.StartInstanceAsync("self-semantics");
        host.Log.Entries.Should().Equal("collecting.entry");

        // Regular $self transition does not run exit/entry, and total=40 does not trigger auto-complete
        inst = await host.TransitionAsync(inst.Id, "add", new { delta = 40 });
        inst.State.Should().Be("collecting");
        host.Log.Entries.Should().Equal("collecting.entry");

        // Reenter $self transition runs execute -> exit -> entry
        inst = await host.TransitionAsync(inst.Id, "refresh");
        inst.State.Should().Be("collecting");
        host.Log.Entries.Should().Equal("collecting.entry", "refresh.execute", "collecting.exit", "collecting.entry");

        // Regular $self transition bringing total to 100 triggers automatic transition to "done"
        inst = await host.TransitionAsync(inst.Id, "add", new { delta = 60 });
        inst.State.Should().Be("done");
        inst.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Scheduled_transitions_support_dynamic_after_and_at_expressions()
    {
        const string def = """
            {
              "key": "dynamic-timers",
              "start": {
                "key": "start",
                "target": "wait-duration",
                "input": {
                  "schema": {
                    "type": "object",
                    "properties": {
                      "sla": { "type": "string" },
                      "deadline": { "type": "string" }
                    }
                  }
                }
              },
              "states": [
                {
                  "key": "wait-duration",
                  "transitions": [
                    {
                      "key": "sla-timer",
                      "target": "wait-timestamp",
                      "trigger": "Scheduled",
                      "after": "{{ data.sla }}"
                    }
                  ]
                },
                {
                  "key": "wait-timestamp",
                  "transitions": [
                    {
                      "key": "at-timer",
                      "target": "expired",
                      "trigger": "Scheduled",
                      "at": "{{ data.deadline }}"
                    }
                  ]
                },
                { "key": "expired", "type": "Finish", "subType": "Timeout" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var deadline = WorkflowTestHost.StartTime.AddHours(3).ToString("O");
        var inst = await host.StartInstanceAsync("dynamic-timers", new { sla = "PT30M", deadline });

        inst.State.Should().Be("wait-duration");
        inst.ScheduledTransitions.Should().ContainSingle()
            .Which.DueAt.Should().Be(WorkflowTestHost.StartTime.AddMinutes(30).UtcDateTime);

        await host.RunJobsAsync(TimeSpan.FromMinutes(30));
        inst = await host.GetAsync(inst.Id);
        inst.State.Should().Be("wait-timestamp");
        inst.ScheduledTransitions.Should().ContainSingle()
            .Which.DueAt.Should().Be(WorkflowTestHost.StartTime.AddHours(3).UtcDateTime);

        await host.RunJobsAsync(TimeSpan.FromHours(2.5));
        inst = await host.GetAsync(inst.Id);
        inst.State.Should().Be("expired");
        inst.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    // ─────────────────── Faz 2: 4-Eyes (Maker-Checker), Dynamic Roles & Data Masking ───────────────────

    [Fact]
    public async Task Maker_checker_denyRoles_and_dynamic_role_expressions_enforce_authorization()
    {
        const string def = """
            {
              "key": "maker-checker",
              "start": {
                "key": "start",
                "target": "draft",
                "input": {
                  "schema": {
                    "type": "object",
                    "properties": {
                      "assignedReviewer": { "type": "string" },
                      "requiredDeptRole": { "type": "string" }
                    }
                  }
                }
              },
              "states": [
                {
                  "key": "draft",
                  "transitions": [
                    {
                      "key": "submit",
                      "target": "review",
                      "roles": ["$starter"]
                    }
                  ]
                },
                {
                  "key": "review",
                  "transitions": [
                    {
                      "key": "approve",
                      "target": "dept-signoff",
                      "roles": ["manager", "$user:{{ data.assignedReviewer }}"],
                      "denyRoles": ["$previousUser"]
                    }
                  ]
                },
                {
                  "key": "dept-signoff",
                  "transitions": [
                    {
                      "key": "signoff",
                      "target": "done",
                      "roles": ["$role:{{ data.requiredDeptRole }}"]
                    }
                  ]
                },
                { "key": "done", "type": "Finish", "subType": "Success" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var alice = WorkflowTestHost.User("alice", "manager");
        var bob = WorkflowTestHost.User("bob");
        var carol = WorkflowTestHost.User("carol", "finance-lead");

        var inst = await host.StartInstanceAsync("maker-checker", new
        {
            assignedReviewer = "bob",
            requiredDeptRole = "finance-lead",
        }, user: alice);

        // Only $starter (alice) can submit
        await FluentActions.Awaiting(() => host.TransitionAsync(inst.Id, "submit", user: bob))
            .Should().ThrowAsync<WorkflowForbiddenException>();

        inst = await host.TransitionAsync(inst.Id, "submit", user: alice);
        inst.State.Should().Be("review");

        // Alice is a manager, but she executed the previous transition ("submit") -> blocked by denyRoles: ["$previousUser"] (4-eyes)
        await FluentActions.Awaiting(() => host.TransitionAsync(inst.Id, "approve", user: alice))
            .Should().ThrowAsync<WorkflowForbiddenException>();

        // Bob is not a manager, but matches "$user:{{ data.assignedReviewer }}" and is not $previousUser -> allowed!
        inst = await host.TransitionAsync(inst.Id, "approve", user: bob);
        inst.State.Should().Be("dept-signoff");

        // Dept signoff requires "$role:{{ data.requiredDeptRole }}" ("finance-lead")
        await FluentActions.Awaiting(() => host.TransitionAsync(inst.Id, "signoff", user: bob))
            .Should().ThrowAsync<WorkflowForbiddenException>();

        inst = await host.TransitionAsync(inst.Id, "signoff", user: carol);
        inst.State.Should().Be("done");
    }

    [Fact]
    public async Task DataSchema_projects_role_restricted_and_masked_fields_per_caller()
    {
        const string def = """
            {
              "key": "masked-workflow",
              "dataSchema": {
                "type": "object",
                "properties": {
                  "customerName": { "type": "string" },
                  "iban": { "type": "string", "x-mask": { "pattern": "last4", "exceptRoles": ["auditor"] } },
                  "email": { "type": "string", "x-mask": { "pattern": "email", "exceptRoles": ["auditor"] } },
                  "internalRiskNote": { "type": "string", "x-roles": ["auditor", "risk"] }
                }
              },
              "start": {
                "key": "start",
                "target": "active",
                "input": {
                  "schema": {
                    "type": "object",
                    "properties": {
                      "customerName": { "type": "string" },
                      "iban": { "type": "string" },
                      "email": { "type": "string" },
                      "internalRiskNote": { "type": "string" }
                    }
                  }
                }
              },
              "states": [
                { "key": "active" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var agent = WorkflowTestHost.User("agent1", "agent");
        var auditor = WorkflowTestHost.User("auditor1", "auditor");

        var started = await host.StartInstanceAsync("masked-workflow", new
        {
            customerName = "Ayşe Yılmaz",
            iban = "TR330006100519786457841326",
            email = "ayse@example.com",
            internalRiskNote = "VIP-LOW-RISK",
        }, user: agent);

        // Agent sees masked iban/email and does NOT see internalRiskNote
        started.Data["customerName"]!.GetValue<string>().Should().Be("Ayşe Yılmaz");
        started.Data["iban"]!.GetValue<string>().Should().EndWith("1326").And.StartWith("**********************");
        started.Data["email"]!.GetValue<string>().Should().Be("a***@example.com");
        started.Data.ContainsKey("internalRiskNote").Should().BeFalse();

        // Auditor sees unmasked iban/email and sees internalRiskNote
        var auditorView = await host.GetAsync(started.Id, user: auditor);
        auditorView.Data["iban"]!.GetValue<string>().Should().Be("TR330006100519786457841326");
        auditorView.Data["email"]!.GetValue<string>().Should().Be("ayse@example.com");
        auditorView.Data["internalRiskNote"]!.GetValue<string>().Should().Be("VIP-LOW-RISK");
    }

    // ─────────────────── Faz 3: FanOutTask, CacheTask & ActiveSubFlow Forwarding ───────────────────

    [Fact]
    public async Task FanOut_and_Cache_tasks_execute_and_populate_instance_data()
    {
        const string def = """
            {
              "key": "fanout-and-cache",
              "start": {
                "key": "start",
                "target": "done",
                "input": {
                  "schema": {
                    "type": "object",
                    "properties": {
                      "lines": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "sku": { "type": "string" },
                            "price": { "type": "number" }
                          }
                        }
                      }
                    }
                  }
                },
                "onExecute": [
                  {
                    "key": "compute-lines",
                    "task": "fanOut",
                    "config": {
                      "items": "data.lines",
                      "maxConcurrency": 2,
                      "into": "lineTotals",
                      "subTask": {
                        "task": "set",
                        "config": {
                          "values": {
                            "result": "item.sku + \":\" + (item.price * (item.index + 1))"
                          }
                        }
                      }
                    }
                  },
                  {
                    "key": "cached-lookup-1",
                    "task": "cache",
                    "config": {
                      "key": "rate:USD",
                      "ttl": "PT10M",
                      "into": "firstLookup",
                      "subTask": {
                        "key": "expensive-op",
                        "task": "flaky",
                        "config": {
                          "failTimes": 0,
                          "into": "flakyCalls"
                        }
                      }
                    }
                  },
                  {
                    "key": "cached-lookup-2",
                    "task": "cache",
                    "config": {
                      "key": "rate:USD",
                      "ttl": "PT10M",
                      "into": "secondLookup",
                      "subTask": {
                        "key": "expensive-op",
                        "task": "flaky",
                        "config": {
                          "failTimes": 0,
                          "into": "flakyCalls"
                        }
                      }
                    }
                  }
                ]
              },
              "states": [
                { "key": "done", "type": "Finish", "subType": "Success" }
              ]
            }
            """;

        await using var host = await WorkflowTestHost.StartAsync(def);
        var inst = await host.StartInstanceAsync("fanout-and-cache", new
        {
            lines = new[]
            {
                new { sku = "A", price = 10 },
                new { sku = "B", price = 20 },
                new { sku = "C", price = 30 },
            },
        });

        inst.State.Should().Be("done");
        var totals = inst.Data["lineTotals"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        totals.Should().Equal("A:10", "B:40", "C:90");

        // CacheTask: the underlying flaky counter was only incremented once because the second call hit the cache!
        var counter = host.Services.GetRequiredService<FlakyCounter>();
        counter.Calls("expensive-op").Should().Be(1);
        inst.Data["firstLookup"]!.GetValue<int>().Should().Be(1);
        inst.Data["secondLookup"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task ActiveSubFlow_is_projected_and_parent_forwards_child_transitions()
    {
        await using var host = await WorkflowTestHost.StartAsync(
            TestWorkflows.ParentAsync,
            TestWorkflows.ChildManual);

        var parent = await host.StartInstanceAsync("parent-async", new { name = "ann" });
        parent.State.Should().Be("waiting-child");
        parent.ActiveSubFlow.Should().NotBeNull();
        parent.ActiveSubFlow!.Workflow.Should().Be("child-manual");
        parent.ActiveSubFlow.State.Should().Be("pending");
        parent.ActiveSubFlow.AvailableTransitions.Select(t => t.Key).Should().Contain("complete");

        // Execute child transition "complete" directly through the parent instance ID!
        var updatedParent = await host.TransitionAsync(parent.Id, "complete", new { ok = true });
        updatedParent.State.Should().Be("approved");
        updatedParent.Status.Should().Be(WorkflowInstanceStatus.Completed);
        updatedParent.ActiveSubFlow.Should().BeNull();
        updatedParent.Data["answer"]!["ok"]!.GetValue<bool>().Should().BeTrue();
    }

    // ─────────────────── Faz 4: Task-Level Execution Trace & Conditional GET (ETag / 304) ───────────────────

    [Fact]
    public async Task History_records_task_level_execution_traces_and_GET_supports_ETag_304()
    {
        await using var host = await WorkflowTestHost.StartAsync(TestWorkflows.Pipeline);
        var inst = await host.StartInstanceAsync("pipeline", new { name = "bike", amount = 500 });
        inst = await host.TransitionAsync(inst.Id, "submit", new { comment = "ready" });

        var history = await host.HistoryAsync(inst.Id);
        history.Should().HaveCount(2);

        var startHop = history[0];
        startHop.TransitionKey.Should().Be("start");
        startHop.Tasks.Select(t => (t.Hook, t.TaskType, t.Outcome)).Should().Equal(
            ("onExecute", "record", "Completed"),
            ("onEntry", "record", "Completed"));

        var submitHop = history[1];
        submitHop.TransitionKey.Should().Be("submit");
        submitHop.Tasks.Select(t => (t.Hook, t.TaskType, t.Outcome)).Should().Equal(
            ("onExecute", "record", "Completed"),
            ("onExit", "record", "Completed"),
            ("onEntry", "record", "Completed"),
            ("onEntry", "record", "Completed"));

        // Conditional GET (ETag / If-None-Match -> 304 Not Modified)
        using var get1 = await host.Client.SendAsync(
            host.Request(HttpMethod.Get, $"/api/workflows/pipeline/instances/{inst.Id}"),
            TestContext.Current.CancellationToken);
        get1.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag1 = get1.Headers.ETag?.Tag;
        etag1.Should().NotBeNullOrWhiteSpace();

        using var get304Req = host.Request(HttpMethod.Get, $"/api/workflows/pipeline/instances/{inst.Id}");
        get304Req.Headers.TryAddWithoutValidation("If-None-Match", etag1);
        using var get304 = await host.Client.SendAsync(get304Req, TestContext.Current.CancellationToken);
        get304.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // After a state change, the old ETag no longer matches -> returns 200 OK with a new ETag
        await host.TransitionAsync(inst.Id, "approve", user: WorkflowTestHost.User("mia", "manager"));

        using var getAfterChangeReq = host.Request(HttpMethod.Get, $"/api/workflows/pipeline/instances/{inst.Id}");
        getAfterChangeReq.Headers.TryAddWithoutValidation("If-None-Match", etag1);
        using var getAfterChange = await host.Client.SendAsync(getAfterChangeReq, TestContext.Current.CancellationToken);
        getAfterChange.StatusCode.Should().Be(HttpStatusCode.OK);
        getAfterChange.Headers.ETag?.Tag.Should().NotBe(etag1);
    }
}
