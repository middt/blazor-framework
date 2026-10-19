using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Tests.Workflow;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Server;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Ui;

/// <summary>
/// Renders <c>view.layout</c> states from an in-memory dictionary with the real <see cref="UiHtmlRenderer"/> (like the sample's
/// renderer, without a store) and records the calls.
/// </summary>
public sealed class FakeWorkflowLayoutRenderer(ViewRendererLog log) : IWorkflowViewRenderer
{
    public static readonly ConcurrentDictionary<string, string> Layouts = new(StringComparer.Ordinal);

    private static readonly MiddtUiOptions UiOptions = new();
    private static readonly UiHtmlRenderer Renderer = new(UiOptions.Widgets, Microsoft.Extensions.Options.Options.Create(UiOptions));

    public ConcurrentQueue<(string Layout, string State, string? User)> Calls { get; } = new();

    public Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser user, CancellationToken cancellationToken = default)
    {
        log.Calls.Enqueue("layout");
        if (instance.Layout is not { } layout)
        {
            return Task.FromResult<string?>(null);
        }

        Calls.Enqueue((layout, instance.State, user.Name));
        if (!Layouts.TryGetValue(layout, out var json))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(Renderer.Render(UiJson.Parse(json), new Dictionary<string, JsonNode?>
        {
            [UiExpressions.Data] = instance.Data.DeepClone(),
            [UiExpressions.Instance] = new JsonObject { ["state"] = instance.State, ["workflow"] = instance.Workflow },
        }, user, CultureInfo.InvariantCulture));
    }
}

/// <summary>Order in which the registered view renderers were asked.</summary>
public sealed class ViewRendererLog
{
    public ConcurrentQueue<string> Calls { get; } = new();
}

/// <summary>A renderer that handles one state with fixed HTML.</summary>
public sealed class FixedViewRenderer(string name, string state, ViewRendererLog log) : IWorkflowViewRenderer
{
    public Task<string?> RenderAsync(WorkflowInstanceModel instance, WorkflowUser user, CancellationToken cancellationToken = default)
    {
        log.Calls.Enqueue(name);
        return Task.FromResult(instance.State == state ? $"<p>{name}</p>" : null);
    }
}

public sealed class WorkflowLayoutTests
{
    private const string LayoutWorkflow = """
        { "key": "layout-flow",
          "start": { "key": "start", "target": "review",
                     "input": { "schema": { "type": "object", "properties": { "customer": { "type": "string" } } } } },
          "states": [
            { "key": "review", "title": "Review", "view": { "layout": "wf-review" }, "transitions": [ { "key": "next", "target": "missing" } ] },
            { "key": "missing", "view": { "layout": "wf-missing" }, "transitions": [ { "key": "next", "target": "plain" } ] },
            { "key": "plain", "transitions": [ { "key": "finish", "target": "done" } ] },
            { "key": "done", "type": "Finish" } ] }
        """;

    private const string ReviewLayout = """
        { "key": "wf-review", "kind": "view", "body": [
          { "widget": "heading", "text": "Customer {{ data.customer }}" },
          { "widget": "text", "text": "{{ instance.workflow }}/{{ instance.state }} for {{ user.Name }}" } ] }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<WorkflowTestHost> Host(bool withRenderer = true, Action<IServiceCollection>? extra = null) =>
        WorkflowTestHost.StartAsync([LayoutWorkflow], services: s =>
        {
            if (withRenderer)
            {
                FakeWorkflowLayoutRenderer.Layouts["wf-review"] = ReviewLayout;
                s.AddSingleton<ViewRendererLog>();
                s.AddSingleton<FakeWorkflowLayoutRenderer>();
                s.AddSingleton<IWorkflowViewRenderer>(sp => sp.GetRequiredService<FakeWorkflowLayoutRenderer>());
            }

            extra?.Invoke(s);
        });

    private static async Task<string?> RenderAsync(WorkflowTestHost host, WorkflowInstanceModel instance, WorkflowUser? user = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowViewRenderer>().RenderAsync(instance, user, Ct);
    }

    [Fact]
    public async Task Engine_exposes_the_layout_of_the_current_state()
    {
        await using var host = await Host();

        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });
        var missing = await host.TransitionAsync(instance.Id, "next");
        var plain = await host.TransitionAsync(instance.Id, "next");

        instance.Layout.Should().Be("wf-review");
        instance.View.Should().BeNull();
        missing.Layout.Should().Be("wf-missing");
        plain.Layout.Should().BeNull();
        (await host.GetAsync(instance.Id)).Layout.Should().BeNull();
    }

    [Fact]
    public async Task Renderer_delegates_layouts_to_the_registered_view_renderer()
    {
        await using var host = await Host();
        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "<b>Ada</b>" });

        var html = await RenderAsync(host, instance, WorkflowTestHost.User("bob"));

        html.Should().Be("<div class=\"middt-ui\" data-layout=\"wf-review\"><h2 class=\"middt-ui-heading\">Customer &lt;b&gt;Ada&lt;/b&gt;</h2>"
            + "<p class=\"middt-ui-text middt-ui-text-normal\">layout-flow/review for bob</p></div>");
        host.Services.GetRequiredService<FakeWorkflowLayoutRenderer>().Calls.Should().ContainSingle().Which.Should().Be(("wf-review", "review", "bob"));
    }

    [Fact]
    public async Task Renderer_uses_the_system_user_when_none_is_given()
    {
        await using var host = await Host();
        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });

        (await RenderAsync(host, instance)).Should().Contain("for system");
    }

    [Fact]
    public async Task Renderer_returns_null_without_a_view_renderer_or_for_unknown_layouts()
    {
        await using (var withoutRenderer = await Host(withRenderer: false))
        {
            var instance = await withoutRenderer.StartInstanceAsync("layout-flow", new { customer = "Ada" });
            (await RenderAsync(withoutRenderer, instance)).Should().BeNull();
        }

        await using var host = await Host();
        var started = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });
        var missing = await host.TransitionAsync(started.Id, "next");
        (await RenderAsync(host, missing)).Should().BeNull("the layout renderer does not know wf-missing");
    }

    [Fact]
    public async Task View_endpoint_returns_the_layout_html_for_the_caller()
    {
        await using var host = await Host();
        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });

        using var request = host.Request(HttpMethod.Get, $"api/workflows/layout-flow/instances/{instance.Id}/view", null, "carol", "clerk");
        using var response = await host.Client.SendAsync(request, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Contain("Customer Ada").And.Contain("layout-flow/review for carol");
    }

    [Fact]
    public async Task View_endpoint_returns_404_when_the_layout_cannot_be_rendered()
    {
        await using var host = await Host(withRenderer: false);
        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });

        using var request = host.Request(HttpMethod.Get, $"api/workflows/layout-flow/instances/{instance.Id}/view", null, "alice");
        using var response = await host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Definition_and_instance_endpoints_expose_the_layout()
    {
        await using var host = await Host();
        var instance = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });

        using var definitionRequest = host.Request(HttpMethod.Get, "api/workflows/layout-flow", null, "alice");
        using var definition = await host.Client.SendAsync(definitionRequest, Ct);
        using var instanceRequest = host.Request(HttpMethod.Get, $"api/workflows/layout-flow/instances/{instance.Id}", null, "alice");
        using var single = await host.Client.SendAsync(instanceRequest, Ct);

        var model = (await definition.Content.ReadFromJsonAsync<BaseResponseDataModel<WorkflowDefinitionModel>>(WorkflowTestHost.Json, Ct))!.Data!;
        model.States.Single(s => s.Key == "review").Layout.Should().Be("wf-review");
        model.States.Single(s => s.Key == "plain").Layout.Should().BeNull();
        (await single.Content.ReadFromJsonAsync<BaseResponseDataModel<WorkflowInstanceModel>>(WorkflowTestHost.Json, Ct))!.Data!.Layout.Should().Be("wf-review");
    }

    [Fact]
    public async Task Renderers_are_tried_in_registration_order_and_the_first_html_wins()
    {
        await using var host = await Host(extra: s =>
        {
            s.AddSingleton<IWorkflowViewRenderer>(sp => new FixedViewRenderer("review-late", "review", sp.GetRequiredService<ViewRendererLog>()));
            s.AddSingleton<IWorkflowViewRenderer>(sp => new FixedViewRenderer("plain", "plain", sp.GetRequiredService<ViewRendererLog>()));
        });
        var log = host.Services.GetRequiredService<ViewRendererLog>();
        var review = await host.StartInstanceAsync("layout-flow", new { customer = "Ada" });

        (await RenderAsync(host, review)).Should().Contain("Customer Ada", "the layout renderer is registered before the fixed renderers");
        log.Calls.Should().Equal("layout");

        log.Calls.Clear();
        var missing = await host.TransitionAsync(review.Id, "next");
        (await RenderAsync(host, missing)).Should().BeNull();
        log.Calls.Should().Equal("layout", "review-late", "plain");

        log.Calls.Clear();
        var plain = await host.TransitionAsync(review.Id, "next");
        (await RenderAsync(host, plain)).Should().Be("<p>plain</p>");
        log.Calls.Should().Equal("layout", "review-late", "plain");
    }
}
