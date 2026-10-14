using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowDefinitionTests
{
    private static string SampleDefinition(string file) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "demo", "server", "Middt.Sample.Api", "Workflows", file));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Loads the definitions (without a database) and returns the registry, or the validation errors.</summary>
    private static (WorkflowRegistry? Registry, IReadOnlyList<string> Errors) Load(params string[] definitions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            for (var i = 0; i < definitions.Length; i++)
            {
                o.AddDefinition(definitions[i], $"def{i}.json");
            }

            o.AddTask<RecordTask>("record");
        });
        services.AddSingleton<TaskLog>();

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WorkflowRegistry>();
        try
        {
            registry.EnsureLoaded();
            return (registry, []);
        }
        catch (WorkflowDefinitionException ex)
        {
            return (null, ex.Errors);
        }
    }

    private static IReadOnlyList<string> Errors(params string[] definitions) => Load(definitions).Errors;

    private const string Valid = """
        {
          // comments and trailing commas are allowed
          "key": "valid",
          "start": { "key": "start", "target": "a" },
          "states": [
            { "key": "a", "transitions": [ { "key": "go", "target": "b" }, ] },
            { "key": "b", "type": "Finish", "subType": "Success" }
          ]
        }
        """;

    [Fact]
    public void Sample_definitions_load_without_errors()
    {
        var (registry, errors) = Load(SampleDefinition("order-approval.json"), SampleDefinition("credit-check.json"));

        errors.Should().BeEmpty();
        var order = registry!.GetLatest("order-approval");
        order.Version.Should().Be(1);
        order.Start.Target.Should().Be("credit-check");
        order.FindState("credit-check")!.Type.Should().Be(StateType.SubFlow);
        order.FindState("manager-review")!.Transitions.Should().Contain(t => t.Key == "escalate" && t.Trigger == TriggerType.Scheduled);
        order.SharedTransitions.Should().ContainSingle(t => t.Key == "cancel" && t.AvailableIn.Contains("*"));
        registry.Latest.Select(d => d.Key).Should().BeEquivalentTo("order-approval", "credit-check");
    }

    [Fact]
    public void All_sample_definition_versions_load_together_and_the_wizard_is_latest()
    {
        var files = Directory.GetFiles(Path.Combine(RepositoryRoot(), "demo", "server", "Middt.Sample.Api", "Workflows"), "*.json")
            .Select(f => SampleDefinition(Path.GetFileName(f)))
            .ToArray();

        var (registry, errors) = Load(files);

        errors.Should().BeEmpty();
        var latest = registry!.GetLatest("order-approval");
        latest.Version.Should().Be(2);
        latest.Start.Target.Should().Be("customer");
        latest.States.Where(s => s.Step is not null).Select(s => s.Step).Distinct().Should().BeEquivalentTo(new int?[] { 1, 2, 3, 4, 5 });
        registry.Get("order-approval", 1).Start.Target.Should().Be("credit-check", "running v1 instances keep their definition");
        registry.GetLatest("credit-check").Version.Should().Be(2);
    }

    [Fact]
    public void Sample_with_subflow_fails_when_child_workflow_is_missing()
    {
        Errors(SampleDefinition("order-approval.json"))
            .Should().ContainSingle(e => e.Contains("subflow workflow 'credit-check' is not defined", StringComparison.Ordinal));
    }

    [Fact]
    public void Minimal_definition_with_comments_and_trailing_commas_is_valid()
    {
        Errors(Valid).Should().BeEmpty();
    }

    public static TheoryData<string, string> InvalidDefinitions => new()
    {
        // unknown JSON property (typo) is rejected instead of being silently ignored
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "trasitions": [] } ] }""", "trasitions" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "a", "trigger": "Automatc", "rule": "true" } ] } ] }""", "w" },
        { """{ "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "'key' is required" },
        { """{ "key": "w", "version": 0, "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "version must be ≥ 1" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [] }""", "at least one state is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" }, { "key": "a" } ] }""", "state 'a' is defined more than once" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "a" }, { "key": "go", "target": "a" } ] } ] }""", "state 'a': transition 'go' is defined more than once" },
        { """{ "key": "w", "start": { "key": "start" }, "states": [ { "key": "a" } ] }""", "start: target '' is not a state" },
        { """{ "key": "w", "start": { "key": "start", "target": "nope" }, "states": [ { "key": "a" } ] }""", "start: target 'nope' is not a state" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "zzz" } ] } ] }""", "transition 'go': target 'zzz' is not a state" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "", "target": "a" } ] } ] }""", "key is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "b" } ] }, { "key": "b", "type": "Finish", "transitions": [ { "key": "back", "target": "a" } ] } ] }""", "state 'b': finish states cannot have transitions" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "a", "trigger": "Automatic" } ] } ] }""", "automatic transitions need a 'rule'" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "x", "target": "b", "trigger": "Automatic", "default": true }, { "key": "y", "target": "b", "trigger": "Automatic", "default": true } ] }, { "key": "b", "type": "Finish" } ] }""", "only one default automatic transition is allowed" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "t", "target": "a", "trigger": "Scheduled", "after": "soon" } ] } ] }""", "scheduled transitions need a valid 'after' duration" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "t", "target": "a", "trigger": "Scheduled" } ] } ] }""", "scheduled transitions need a valid 'after' duration" },
        { """{ "key": "w", "timeout": { "after": "P1X", "target": "a" }, "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "timeout.after is not a valid duration" },
        { """{ "key": "w", "timeout": { "after": "PT1H", "target": "ghost" }, "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "timeout.target 'ghost' is not a state" },
        { """{ "key": "w", "onError": { "delay": "abc" }, "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "onError: delay is not a valid duration" },
        { """{ "key": "w", "onError": { "maxAttempts": 0 }, "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ] }""", "onError: maxAttempts must be ≥ 1" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "a", "trigger": "Automatic", "rule": "data.amount >" } ] } ] }""", "transition 'go': rule:" },
        { """{ "key": "w", "start": { "key": "start", "target": "a", "input": { "set": { "x": "data.(" } } }, "states": [ { "key": "a" } ] }""", "start: input.set 'x':" },
        { """{ "key": "w", "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object", "oneOf": [] } } }, "states": [ { "key": "a" } ] }""", "unsupported schema keyword 'oneOf'" },
        { """{ "key": "w", "start": { "key": "start", "target": "a", "input": { "schema": { "type": "object", "properties": { "code": { "type": "string", "pattern": "([a-z" } } } } }, "states": [ { "key": "a" } ] }""", "$.code: invalid pattern" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "nope" } ] } ] }""", "state 'a': task 'nope': unknown task type 'nope'" },
        { """{ "key": "w", "start": { "key": "start", "target": "a", "onExecute": [ { "task": "record", "when": "data.x ==" } ] }, "states": [ { "key": "a" } ] }""", "task 'record': when:" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "http", "config": { "url": "https://{{ data.host }}/api" } } ] } ] }""", "must not contain placeholders in the scheme or host" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "http", "config": { "url": "{{ data.url }}" } } ] } ] }""", "must be absolute" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "http", "config": { } } ] } ] }""", "http: 'url' is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "set", "config": { } } ] } ] }""", "set: 'values' object is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "set", "config": { "values": { "x": "1 +" } } } ] } ] }""", "set: value of 'x' is not a valid expression" },
        // config values of the wrong JSON type are definition errors, not loader crashes
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "set", "config": { "values": { "x": 5 } } } ] } ] }""", "task 'set': invalid configuration value" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "http", "config": { "url": 42 } } ] } ] }""", "task 'http': invalid configuration value" },
        { """{ "key": "w", "start": { "key": "start", "target": "a", "input": { "schema": { "type": "string", "pattern": 1 } } }, "states": [ { "key": "a" } ] }""", "input.schema invalid configuration value" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onExit": [ { "task": "publish", "config": { "payload": {} } } ] } ] }""", "publish: 'type' is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "log", "config": { } } ] } ] }""", "log: 'message' is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "startWorkflow", "config": { } } ] } ] }""", "startWorkflow: 'workflow' is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "type": "SubFlow", "subFlow": { "workflow": "ghost" } } ] }""", "subflow workflow 'ghost' is not defined" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "type": "SubFlow" } ] }""", "subFlow.workflow is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a", "onEntry": [ { "task": "record", "onError": { "action": "Transition", "transition": "nowhere" } } ] } ] }""", "onError: transition 'nowhere' is not defined in this workflow" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ], "sharedTransitions": [ { "key": "cancel", "target": "a" } ] }""", "shared transition 'cancel': availableIn is required" },
        { """{ "key": "w", "start": { "key": "start", "target": "a" }, "states": [ { "key": "a" } ], "sharedTransitions": [ { "key": "cancel", "target": "a", "availableIn": [ "zz" ] } ] }""", "availableIn state 'zz' does not exist" },
    };

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void Invalid_definition_is_rejected_with_a_clear_error(string json, string expected)
    {
        Errors(json).Should().Contain(e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Errors_are_prefixed_with_workflow_and_version_and_all_problems_are_reported()
    {
        var errors = Errors("""
            { "key": "multi", "version": 3, "start": { "key": "start", "target": "x" },
              "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "y" } ] } ] }
            """);

        errors.Should().Contain(e => e.Contains("start: target 'x'", StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains("target 'y' is not a state", StringComparison.Ordinal));
        errors.Should().AllSatisfy(e => e.Should().StartWith("multi v3: "));
    }

    [Fact]
    public void Json_syntax_errors_mention_the_source()
    {
        Errors("{ \"key\": ").Should().ContainSingle().Which.Should().StartWith("def0.json: ");
    }

    [Fact]
    public void Same_key_and_version_twice_is_rejected()
    {
        Errors(Valid, Valid).Should().Contain(e => e.Contains("workflow 'valid' version 1 is defined twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Error_policy_transition_may_target_any_transition_of_the_workflow()
    {
        Errors("""
            { "key": "w", "start": { "key": "start", "target": "a" },
              "states": [
                { "key": "a", "onEntry": [ { "task": "record", "onError": { "action": "Transition", "transition": "fail" } } ],
                  "transitions": [ { "key": "fail", "target": "b" } ] },
                { "key": "b", "type": "Finish" } ] }
            """).Should().BeEmpty();
    }

    [Fact]
    public void Several_versions_are_indexed_and_latest_is_the_highest()
    {
        var v2 = Valid.Replace("\"key\": \"valid\",", "\"key\": \"valid\", \"version\": 2, \"title\": \"second\",", StringComparison.Ordinal);
        var (registry, errors) = Load(v2, Valid);

        errors.Should().BeEmpty();
        registry!.GetLatest("valid").Version.Should().Be(2);
        registry.Get("valid", 1).Title.Should().BeNull();
        registry.Get("valid", 2).Title.Should().Be("second");
        registry.Latest.Should().ContainSingle();
        FluentActions.Invoking(() => registry.Get("valid", 3)).Should().Throw<WorkflowNotFoundException>();
        FluentActions.Invoking(() => registry.GetLatest("other")).Should().Throw<WorkflowNotFoundException>();
    }

    [Fact]
    public async Task New_instances_use_the_latest_version_and_running_instances_keep_theirs()
    {
        const string v1 = """
            { "key": "versioned", "version": 1, "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "old-end" } ] }, { "key": "old-end", "type": "Finish" } ] }
            """;
        const string v2 = """
            { "key": "versioned", "version": 2, "start": { "key": "start", "target": "a" },
              "states": [ { "key": "a", "transitions": [ { "key": "go", "target": "new-end" } ] }, { "key": "new-end", "type": "Finish" } ] }
            """;

        await using var first = await WorkflowTestHost.StartAsync(v1);
        var old = await first.StartInstanceAsync("versioned");
        old.WorkflowVersion.Should().Be(1);

        // "Deploy" version 2 on the same database.
        await using var second = await WorkflowTestHost.StartAsync([v1, v2], connection: first.Connection);
        var fresh = await second.StartInstanceAsync("versioned");
        fresh.WorkflowVersion.Should().Be(2);

        (await second.TransitionAsync(old.Id, "go")).State.Should().Be("old-end");
        (await second.TransitionAsync(fresh.Id, "go")).State.Should().Be("new-end");
    }

    [Fact]
    public async Task Host_fails_fast_at_startup_with_invalid_definitions()
    {
        var act = () => WorkflowTestHost.StartAsync("""{ "key": "broken", "start": { "key": "start", "target": "nope" }, "states": [ { "key": "a" } ] }""");

        (await act.Should().ThrowAsync<WorkflowDefinitionException>()).Which.Message.Should().Contain("start: target 'nope'");
    }
}
