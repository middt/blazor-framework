using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Workflow.Razor;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

[WorkflowView("test-state")]
public sealed class TestStateView : WorkflowViewBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", Interactive ? "interactive" : "static");
        builder.AddContent(2, $"Customer: {Text("customer")} / {Text("missing", "n/a")}");
        builder.CloseElement();
    }
}

[WorkflowView("test-form")]
public sealed class TestFormView : WorkflowTransitionViewBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Transition.Key);
}

public sealed class NotAView : ComponentBase;

public sealed class WorkflowViewTests
{
    private const string ViewsWorkflow = """
        { "key": "views",
          "start": { "key": "start", "target": "review",
                     "input": { "schema": { "type": "object", "properties": { "customer": { "type": "string" } } } } },
          "states": [
            { "key": "review", "view": { "component": "test-state" }, "transitions": [
                { "key": "decide", "target": "plain", "view": { "component": "test-form" } } ] },
            { "key": "plain", "transitions": [ { "key": "finish", "target": "done" } ] },
            { "key": "done", "type": "Finish" } ] }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<KeyValuePair<string, Type>> Views => WorkflowViewCatalog.FromAssemblyOf<TestStateView>().Views;

    private static IReadOnlyList<string> Errors(string definition, bool registerViews = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db => db.UseSqlite("DataSource=:memory:"));
            o.EnableJobProcessor = false;
            o.AddDefinition(definition);
            if (registerViews)
            {
                o.UseViews(Views);
            }
        });

        try
        {
            services.BuildServiceProvider().GetRequiredService<WorkflowRegistry>().EnsureLoaded();
            return [];
        }
        catch (WorkflowDefinitionException ex)
        {
            return ex.Errors;
        }
    }

    [Fact]
    public void Catalog_discovers_annotated_views_by_kind()
    {
        var catalog = WorkflowViewCatalog.FromAssemblyOf<TestStateView>();

        catalog.Find("test-state").Should().Be<TestStateView>();
        catalog.IsStateView("test-state").Should().BeTrue();
        catalog.IsTransitionView("test-form").Should().BeTrue();
        catalog.IsStateView("test-form").Should().BeFalse();
        catalog.Find("unknown").Should().BeNull();
    }

    [Fact]
    public void Catalog_rejects_non_view_components_and_conflicting_names()
    {
        var catalog = new WorkflowViewCatalog();

        FluentActions.Invoking(() => catalog.Add("x", typeof(NotAView))).Should().Throw<ArgumentException>();
        catalog.Add("same", typeof(TestStateView));
        FluentActions.Invoking(() => catalog.Add("same", typeof(TestFormView))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registered_views_are_accepted() => Errors(ViewsWorkflow).Should().BeEmpty();

    [Fact]
    public void Unknown_view_names_fail_at_startup() =>
        Errors(ViewsWorkflow.Replace("test-state", "nope", StringComparison.Ordinal))
            .Should().ContainSingle(e => e.Contains("view 'nope' is not registered", StringComparison.Ordinal));

    [Fact]
    public void A_state_cannot_use_a_transition_view_and_vice_versa()
    {
        var swapped = ViewsWorkflow
            .Replace("\"test-state\"", "\"__tmp\"", StringComparison.Ordinal)
            .Replace("\"test-form\"", "\"test-state\"", StringComparison.Ordinal)
            .Replace("\"__tmp\"", "\"test-form\"", StringComparison.Ordinal);

        var errors = Errors(swapped);

        errors.Should().Contain(e => e.Contains("state 'review': view 'test-form' is a transition view", StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains("transition 'decide': view 'test-state' is a state view", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_registered_views_names_are_not_checked_but_must_not_be_empty()
    {
        Errors(ViewsWorkflow.Replace("test-state", "ui-only", StringComparison.Ordinal), registerViews: false).Should().BeEmpty();
        Errors(ViewsWorkflow.Replace("test-state", "", StringComparison.Ordinal), registerViews: false)
            .Should().ContainSingle(e => e.Contains("view.component or view.layout is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Instance_and_available_transitions_expose_their_views()
    {
        await using var host = await WorkflowTestHost.StartAsync([ViewsWorkflow], configure: o => o.UseViews(Views));

        var instance = await host.StartInstanceAsync("views", new { customer = "Ada" });

        instance.View.Should().Be("test-state");
        instance.AvailableTransitions.Should().ContainSingle(t => t.Key == "decide" && t.View == "test-form");

        var plain = await host.TransitionAsync(instance.Id, "decide");
        plain.View.Should().BeNull();
        plain.AvailableTransitions.Should().ContainSingle(t => t.Key == "finish" && t.View == null);
    }

    [Fact]
    public async Task View_endpoint_renders_the_state_view_to_encoded_html()
    {
        await using var host = await WorkflowTestHost.StartAsync([ViewsWorkflow], configure: o => o.UseViews(Views));
        var instance = await host.StartInstanceAsync("views", new { customer = "<b>Ada</b>" });

        using var request = host.Request(HttpMethod.Get, $"api/workflows/views/instances/{instance.Id}/view", null, "alice");
        using var response = await host.Client.SendAsync(request, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Be("<p class=\"static\">Customer: &lt;b&gt;Ada&lt;/b&gt; / n/a</p>");
    }

    [Fact]
    public async Task View_endpoint_returns_404_for_states_without_a_view_and_401_for_anonymous_callers()
    {
        await using var host = await WorkflowTestHost.StartAsync([ViewsWorkflow], configure: o => o.UseViews(Views));
        var instance = await host.StartInstanceAsync("views", new { customer = "Ada" });
        var uri = $"api/workflows/views/instances/{instance.Id}/view";

        using (var anonymous = host.Request(HttpMethod.Get, uri, null, null))
        using (var response = await host.Client.SendAsync(anonymous, Ct))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await host.TransitionAsync(instance.Id, "decide");
        using var request = host.Request(HttpMethod.Get, uri, null, "alice");
        using var noView = await host.Client.SendAsync(request, Ct);
        noView.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Renderer_returns_null_when_the_view_is_not_registered_in_this_host()
    {
        await using var host = await WorkflowTestHost.StartAsync([ViewsWorkflow]);
        var instance = await host.StartInstanceAsync("views", new { customer = "Ada" });

        var html = await host.WithEngineAsync(async _ =>
        {
            await using var scope = host.App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<WorkflowViewRenderer>().RenderAsync(instance);
        });

        html.Should().BeNull();
    }
}
