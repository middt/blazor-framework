using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

/// <summary>Multi-step (wizard) features: steps, inline forms, conditional manual transitions, schema-less input.</summary>
public sealed class WorkflowWizardTests
{
    private const string Wizard = """
        { "key": "wizard",
          "start": { "key": "start", "target": "kind" },
          "states": [
            { "key": "kind", "step": 1, "subType": "Human", "transitions": [
                { "key": "next", "title": "Devam", "target": "route", "view": { "inline": true },
                  "input": { "schema": { "type": "object", "additionalProperties": false, "required": ["type"],
                             "properties": { "type": { "type": "string", "enum": ["A", "B"] } } } } } ] },
            { "key": "route", "transitions": [
                { "key": "to-a", "target": "a", "trigger": "Automatic", "rule": "data.type == \"A\"" },
                { "key": "to-b", "target": "b", "trigger": "Automatic", "default": true } ] },
            { "key": "a", "step": 2, "transitions": [
                { "key": "next", "target": "details", "view": { "inline": true },
                  "input": { "into": "a", "schema": { "type": "object", "properties": { "x": { "type": "string" } } } } },
                { "key": "back", "target": "kind" } ] },
            { "key": "b", "step": 2, "transitions": [ { "key": "next", "target": "details" }, { "key": "back", "target": "kind" } ] },
            { "key": "details", "step": 3, "transitions": [
                { "key": "back-a", "title": "Geri", "target": "a", "rule": "data.type == \"A\"" },
                { "key": "back-b", "title": "Geri", "target": "b", "rule": "data.type == \"B\"" },
                { "key": "broken", "target": "done", "rule": "data.type.Length > data.missing.Length" },
                { "key": "touch", "target": "$self", "input": { "set": { "touchedBy": "user.Name" } } },
                { "key": "finish", "target": "done" } ] },
            { "key": "done", "step": 4, "type": "Finish" } ] }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<WorkflowTestHost> Host() => WorkflowTestHost.StartAsync([Wizard]);

    private static async Task<WorkflowInstanceModel> AtDetailsAsync(WorkflowTestHost host, string type)
    {
        var instance = await host.StartInstanceAsync("wizard");
        await host.TransitionAsync(instance.Id, "next", new { type });
        return await host.TransitionAsync(instance.Id, "next", type == "A" ? new { x = "1" } : null);
    }

    [Fact]
    public async Task Inline_transitions_expose_their_ui_hints_and_input_path()
    {
        await using var host = await Host();

        var instance = await host.StartInstanceAsync("wizard");
        var next = instance.AvailableTransitions.Should().ContainSingle(t => t.Key == "next").Subject;
        next.Inline.Should().BeTrue();
        next.View.Should().BeNull("inline without component uses the schema generated form");
        next.InputInto.Should().BeNull();

        var a = await host.TransitionAsync(instance.Id, "next", new { type = "A" });
        a.State.Should().Be("a");
        a.AvailableTransitions.Single(t => t.Key == "next").InputInto.Should().Be("a");
        a.AvailableTransitions.Single(t => t.Key == "back").Inline.Should().BeFalse();
    }

    [Theory]
    [InlineData("A", "back-a")]
    [InlineData("B", "back-b")]
    public async Task Conditional_manual_transitions_are_only_offered_when_their_rule_holds(string type, string expectedBack)
    {
        await using var host = await Host();

        var details = await AtDetailsAsync(host, type);

        details.State.Should().Be("details");
        details.AvailableTransitions.Select(t => t.Key).Should().Contain(expectedBack)
            .And.NotContain(type == "A" ? "back-b" : "back-a");
    }

    [Fact]
    public async Task Executing_a_transition_whose_rule_is_false_is_a_conflict()
    {
        await using var host = await Host();
        var details = await AtDetailsAsync(host, "A");

        await FluentActions.Awaiting(() => host.TransitionAsync(details.Id, "back-b"))
            .Should().ThrowAsync<WorkflowConflictException>().WithMessage("*not available for the current data*");

        (await host.TransitionAsync(details.Id, "back-a")).State.Should().Be("a");
    }

    [Fact]
    public async Task A_failing_availability_rule_hides_the_transition_instead_of_breaking_the_instance()
    {
        await using var host = await Host();
        var details = await AtDetailsAsync(host, "B");

        details.AvailableTransitions.Select(t => t.Key).Should().NotContain("broken");
        await FluentActions.Awaiting(() => host.TransitionAsync(details.Id, "broken")).Should().ThrowAsync<WorkflowConflictException>();
        (await host.GetAsync(details.Id)).Status.Should().Be(WorkflowInstanceStatus.Active);
    }

    [Fact]
    public async Task Going_back_keeps_previously_entered_data_for_prefilling()
    {
        await using var host = await Host();
        var details = await AtDetailsAsync(host, "A");

        var back = await host.TransitionAsync(details.Id, "back-a");

        back.State.Should().Be("a");
        back.Data["type"]!.GetValue<string>().Should().Be("A");
        back.Data["a"]!["x"]!.GetValue<string>().Should().Be("1");
    }

    [Fact]
    public async Task Input_without_schema_ignores_the_body_and_only_applies_set_values()
    {
        await using var host = await Host();
        var details = await AtDetailsAsync(host, "B");

        var touched = await host.TransitionAsync(details.Id, "touch", new { type = "A", injected = true });

        touched.State.Should().Be("details");
        touched.Data["type"]!.GetValue<string>().Should().Be("B");
        touched.Data.ContainsKey("injected").Should().BeFalse();
        touched.Data["touchedBy"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Definition_endpoint_returns_step_numbers()
    {
        await using var host = await Host();
        using var request = host.Request(HttpMethod.Get, "api/workflows/wizard", null, "alice");
        using var response = await host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var definition = (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<WorkflowDefinitionModel>>(WorkflowTestHost.Json, Ct))!.Data!;
        definition.States.Where(s => s.Step is not null).Select(s => (s.Key, s.Step))
            .Should().BeEquivalentTo(new[] { ("kind", (int?)1), ("a", 2), ("b", 2), ("details", 3), ("done", 4) });
        definition.States.Single(s => s.Key == "route").Step.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "key": "v", "start": { "key": "start", "target": "s" }, "states": [ { "key": "s", "view": { "inline": true } } ] }""", "view.inline is only supported on transitions")]
    [InlineData("""{ "key": "v", "start": { "key": "start", "target": "s" }, "states": [ { "key": "s", "transitions": [ { "key": "t", "target": "s", "view": { } } ] } ] }""", "view.component or view.layout is required")]
    public void Invalid_view_hints_are_reported(string json, string expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            o.AddDefinition(json);
        });

        var act = () => services.BuildServiceProvider().GetRequiredService<WorkflowRegistry>().EnsureLoaded();

        act.Should().Throw<WorkflowDefinitionException>().Which.Errors.Should().Contain(e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_transition_without_component_is_valid() =>
        FluentActions.Invoking(() =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMiddtWorkflow(o =>
            {
                o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
                o.EnableJobProcessor = false;
                o.AddDefinition(Wizard);
            });
            services.BuildServiceProvider().GetRequiredService<WorkflowRegistry>().EnsureLoaded();
        }).Should().NotThrow();
}
