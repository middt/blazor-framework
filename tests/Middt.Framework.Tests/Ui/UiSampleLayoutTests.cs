using Middt.Framework.Plugin.Rebus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Validation;
using Middt.Framework.Ui.Widgets;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Ui;

/// <summary>Sample layouts and the workflow definitions referencing them.</summary>
public sealed class UiSampleLayoutTests
{
    private const string LayoutWorkflow = """
        { "key": "layouts",
          "start": { "key": "start", "target": "review" },
          "states": [
            { "key": "review", "view": { "layout": "order-review" }, "transitions": [ { "key": "finish", "target": "done" } ] },
            { "key": "done", "type": "Finish" } ] }
        """;

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal static string SampleDirectory(string name) => Path.Combine(RepositoryRoot(), "demo", "server", "Middt.Sample.Api", name);

    private static string[] LayoutFiles() => Directory.GetFiles(SampleDirectory("Layouts"), "*.json");

    public static TheoryData<string> LayoutFileNames() => [.. LayoutFiles().Select(Path.GetFileName).OfType<string>()];

    private static (WorkflowRegistry? Registry, IReadOnlyList<string> Errors) Load(params string[] definitions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            foreach (var definition in definitions)
            {
                o.AddDefinition(definition);
            }

            o.AddRebusTasks();
        });

        try
        {
            var registry = services.BuildServiceProvider().GetRequiredService<WorkflowRegistry>();
            registry.EnsureLoaded();
            return (registry, []);
        }
        catch (WorkflowDefinitionException ex)
        {
            return (null, ex.Errors);
        }
    }

    [Theory]
    [MemberData(nameof(LayoutFileNames))]
    public void Sample_layout_is_valid_and_named_after_its_key(string file)
    {
        var json = File.ReadAllText(Path.Combine(SampleDirectory("Layouts"), file));

        new UiLayoutValidator(UiWidgetCatalog.CreateDefault()).Validate(json).Errors.Should().BeEmpty();
        UiJson.Parse(json).Key.Should().Be(Path.GetFileNameWithoutExtension(file));
    }

    [Fact]
    public void Sample_layout_keys_are_unique_and_survive_a_serialization_round_trip()
    {
        var layouts = LayoutFiles().Select(f => UiJson.Parse(File.ReadAllText(f))).ToList();

        layouts.Should().NotBeEmpty();
        layouts.Select(l => l.Key).Should().OnlyHaveUniqueItems();
        foreach (var layout in layouts)
        {
            var serialized = UiJson.Serialize(layout);
            UiJson.Serialize(UiJson.Parse(serialized)).Should().Be(serialized, $"{layout.Key} must round-trip through the store");
        }
    }

    [Fact]
    public void Sample_workflows_load_and_their_layout_references_exist_as_views()
    {
        var definitions = Directory.GetFiles(SampleDirectory("Workflows"), "*.json").Select(File.ReadAllText).ToArray();
        var layouts = LayoutFiles().Select(f => UiJson.Parse(File.ReadAllText(f))).ToDictionary(l => l.Key, StringComparer.Ordinal);

        var (registry, errors) = Load(definitions);

        errors.Should().BeEmpty();
        var escalated = registry!.GetLatest("sla-escalation").States.Single(s => s.Key == "escalated");
        escalated.View!.Layout.Should().Be("order-escalated");
        escalated.View.Component.Should().BeNull();

        var references = registry.Latest.SelectMany(w => w.States).Select(s => s.View?.Layout).OfType<string>().Distinct().ToList();
        references.Should().NotBeEmpty();
        foreach (var key in references)
        {
            layouts.Should().ContainKey(key);
            layouts[key].Kind.Should().Be(UiLayoutKind.View, $"workflow states embed layout '{key}'");
        }
    }

    [Fact]
    public void State_layouts_are_accepted() => Load(LayoutWorkflow).Errors.Should().BeEmpty();

    [Fact]
    public void Layouts_on_transitions_are_rejected()
    {
        var definition = LayoutWorkflow.Replace(
            "{ \"key\": \"finish\", \"target\": \"done\" }",
            "{ \"key\": \"finish\", \"target\": \"done\", \"view\": { \"layout\": \"finish-form\" } }",
            StringComparison.Ordinal);

        Load(definition).Errors.Should().ContainSingle(e => e.Contains("transition 'finish': view.layout is only supported on states.", StringComparison.Ordinal));
    }

    [Fact]
    public void Component_and_layout_together_are_rejected() =>
        Load(LayoutWorkflow.Replace("{ \"layout\": \"order-review\" }", "{ \"layout\": \"order-review\", \"component\": \"review\" }", StringComparison.Ordinal))
            .Errors.Should().ContainSingle(e => e.Contains("state 'review': use either view.component or view.layout.", StringComparison.Ordinal));

    [Theory]
    [InlineData("Order-Review")]
    [InlineData("../secrets")]
    [InlineData("")]
    public void Invalid_layout_keys_are_rejected(string key) =>
        Load(LayoutWorkflow.Replace("order-review", key, StringComparison.Ordinal))
            .Errors.Should().ContainSingle(e => e.Contains($"view.layout '{key}' is not a valid layout key.", StringComparison.Ordinal));
}
