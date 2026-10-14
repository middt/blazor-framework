using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

/// <summary>
/// <c>input.sensitive</c>: secrets (passwords, one-time codes) are visible to the transition's tasks and expressions but
/// never reach instance data, history, pending (retry) input or job payloads.
/// </summary>
public sealed class WorkflowSensitiveInputTests
{
    private const string Secret = "S3cr3t!-value";

    private const string Login = """
        {
          "key": "sensitive",
          "start": {
            "key": "start", "target": "credentials",
            "input": {
              "schema": { "type": "object", "properties": { "userName": { "type": "string" }, "pin": { "type": "string" } } },
              "sensitive": [ "pin" ]
            },
            "onExecute": [ { "task": "probe", "config": { "field": "pin" } } ]
          },
          "states": [
            {
              "key": "credentials",
              "transitions": [
                { "key": "submit", "target": "done",
                  "input": {
                    "schema": { "type": "object", "required": [ "userName", "password" ],
                                "properties": { "userName": { "type": "string" }, "password": { "type": "string" } } },
                    "into": "login",
                    "sensitive": [ "password" ],
                    "set": { "passwordLength": "input.password.Length" }
                  },
                  "onExecute": [ { "task": "probe", "config": { "field": "password" } } ] },
                { "key": "submit-failing", "target": "done",
                  "input": {
                    "schema": { "type": "object", "properties": { "userName": { "type": "string" }, "password": { "type": "string" } } },
                    "sensitive": [ "password" ]
                  },
                  "onExecute": [ { "task": "fail", "config": { "message": "credential store down" } } ] }
              ]
            },
            { "key": "done", "type": "Finish", "subType": "Success" }
          ]
        }
        """;

    private const string FailingStart = """
        {
          "key": "sensitive-start",
          "start": {
            "key": "start", "target": "s",
            "input": {
              "schema": { "type": "object", "properties": { "userName": { "type": "string" }, "pin": { "type": "string" } } },
              "sensitive": [ "pin" ]
            },
            "onExecute": [ { "task": "fail" } ]
          },
          "states": [ { "key": "s" }, { "key": "end", "type": "Finish" } ]
        }
        """;

    private static Task<WorkflowTestHost> Host() =>
        WorkflowTestHost.StartAsync([Login, FailingStart], o => o.AddTask<ProbeTask>("probe"));

    [Fact]
    public async Task Task_sees_the_sensitive_value_but_data_and_history_do_not()
    {
        await using var host = await Host();
        var started = await host.StartInstanceAsync("sensitive", new { userName = "ann", pin = "1234" });

        var done = await host.TransitionAsync(started.Id, "submit", new { userName = "ann", password = Secret });

        host.Log.Entries.Should().Equal("pin=1234", "password=" + Secret);
        done.State.Should().Be("done");
        done.Data["userName"]!.GetValue<string>().Should().Be("ann");
        done.Data["login"]!["userName"]!.GetValue<string>().Should().Be("ann");
        done.Data["passwordLength"]!.GetValue<int>().Should().Be(Secret.Length, "set expressions see the sensitive input");
        done.Data.ToJsonString().Should().NotContain(Secret).And.NotContain("1234").And.NotContain("password\"");

        var rows = await host.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == started.Id).OrderBy(h => h.Id).ToListAsync(TestContext.Current.CancellationToken));
        rows.Should().HaveCount(2);
        rows[0].Input.Should().Be("""{"userName":"ann"}""");
        rows[1].Input.Should().Be("""{"userName":"ann"}""");
        rows.Should().AllSatisfy(r => (r.Input + r.Data).Should().NotContain(Secret).And.NotContain("1234"));

        var row = await host.RowAsync(started.Id);
        row.Data.Should().NotContain(Secret);
        row.PendingInput.Should().BeNull();
    }

    [Fact]
    public async Task Faulted_transition_keeps_only_the_non_sensitive_pending_input()
    {
        await using var host = await Host();
        var started = await host.StartInstanceAsync("sensitive", new { userName = "ann" });

        var faulted = await host.TransitionAsync(started.Id, "submit-failing", new { userName = "ann", password = Secret });

        faulted.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        var row = await host.RowAsync(started.Id);
        row.PendingTransition.Should().Be("submit-failing");
        row.PendingInput.Should().Be("""{"userName":"ann"}""");
        row.Data.Should().NotContain(Secret);
        var incidents = await host.WithDbAsync(db => db.Incidents.AsNoTracking().Where(i => i.InstanceId == started.Id).ToListAsync(TestContext.Current.CancellationToken));
        incidents.Should().ContainSingle().Which.Error.Should().NotContain(Secret);
    }

    [Fact]
    public async Task Faulted_start_keeps_only_the_non_sensitive_pending_input()
    {
        await using var host = await Host();

        var faulted = await host.StartInstanceAsync("sensitive-start", new { userName = "ann", pin = "9876" });

        faulted.Status.Should().Be(WorkflowInstanceStatus.Faulted);
        var row = await host.RowAsync(faulted.Id);
        row.PendingTransition.Should().Be("start");
        row.PendingInput.Should().Be("""{"userName":"ann"}""");
        row.Data.Should().NotContain("9876");
    }

    [Fact]
    public async Task Sensitive_transitions_cannot_be_queued()
    {
        await using var host = await Host();
        var started = await host.StartInstanceAsync("sensitive", new { userName = "ann" });

        var act = () => host.TransitionAsync(started.Id, "submit", new { userName = "ann", password = Secret }, runAsync: true);

        (await act.Should().ThrowAsync<WorkflowValidationException>()).Which.Errors.Should().ContainSingle()
            .Which.Should().Contain("asynchronously");
        (await host.JobsAsync(started.Id)).Should().NotContain(j => j.Kind == Middt.Framework.Workflow.Persistence.WorkflowJobKind.Transition);
        (await host.RowAsync(started.Id)).Status.Should().Be(WorkflowInstanceStatus.Active);
    }

    [Fact]
    public async Task Merge_without_sensitive_values_leaves_the_input_untouched()
    {
        var definition = new InputDefinition { Sensitive = ["password"] };
        var input = WorkflowTestHost.ToObject(new { userName = "ann", password = Secret })!;

        var persistable = definition.Persistable(input)!;

        persistable.ToJsonString().Should().Be("""{"userName":"ann"}""");
        input["password"]!.GetValue<string>().Should().Be(Secret, "the caller's input (seen by tasks) is not modified");
        definition.Persistable(WorkflowTestHost.ToObject(new { userName = "ann" })).Should().NotBeNull();
        new InputDefinition().Persistable(input).Should().BeSameAs(input);
    }

    [Fact]
    public void Sensitive_names_must_be_declared_schema_properties()
    {
        const string invalid = """
            {
              "key": "bad-sensitive",
              "start": { "key": "start", "target": "a",
                         "input": { "schema": { "type": "object", "properties": { "userName": { "type": "string" } } }, "sensitive": [ "password" ] } },
              "states": [ { "key": "a", "type": "Finish" } ]
            }
            """;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            o.AddDefinition(invalid, "bad.json");
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<WorkflowRegistry>().EnsureLoaded();

        act.Should().Throw<WorkflowDefinitionException>().Which.Errors
            .Should().Contain(e => e.Contains("input.sensitive 'password' is not a property of input.schema", StringComparison.Ordinal));
    }

    /// <summary><c>probe</c>: logs <c>{field}={input value}</c> to prove the task receives the sensitive value.</summary>
    private sealed class ProbeTask(TaskLog log) : IWorkflowTask
    {
        public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
        {
            var field = context.Config["field"]!.GetValue<string>();
            log.Add($"{field}={context.Scope.Input?[field]?.GetValue<string>()}");
            return Task.CompletedTask;
        }
    }
}
