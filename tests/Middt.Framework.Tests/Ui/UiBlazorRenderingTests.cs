using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Web.Ui.Components;
using Middt.Framework.Blazor.Web.Ui.Components.Display;
using Middt.Framework.Blazor.Web.Ui.Rendering;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Server;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Ui;

/// <summary>Custom widget used to prove registration end to end.</summary>
public sealed class StarsWidget : UiWidgetBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "stars");
        builder.AddContent(2, new string('*', Math.Clamp(Node.IntProp("count", 1), 1, 5)) + " " + Text("label"));
        builder.CloseElement();
    }
}

/// <summary>Display components of Middt.Framework.Blazor.Web.Ui rendered with <see cref="HtmlRenderer"/>.</summary>
public sealed class UiBlazorRenderingTests
{
    internal static async Task<string> RenderAsync(UiLayoutDocument layout, IReadOnlyDictionary<string, JsonNode?> variables, bool interactive = false, UiBlazorWidgets? widgets = null, CultureInfo? culture = null)
    {
        widgets ??= UiBlazorWidgets.CreateDefault();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(widgets).BuildServiceProvider();
        var context = new UiRenderContext(widgets.Catalog, UiRenderInput.User, variables, interactive) { Culture = culture ?? CultureInfo.InvariantCulture };

        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<UiLayoutView>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(UiLayoutView.Layout)] = layout,
                [nameof(UiLayoutView.Context)] = context,
            }));
            return output.ToHtmlString();
        });
    }

    private static Task<string> RenderAsync(string body, string? data = null, bool interactive = false, UiBlazorWidgets? widgets = null, string? query = null) =>
        RenderAsync(UiRenderInput.Layout(body), UiRenderInput.Variables(data, query), interactive, widgets);

    [Fact]
    public async Task Display_widgets_render_their_markup()
    {
        var html = await RenderAsync("""
            [ { "widget": "heading", "text": "Hello {{ user.Name }}", "level": 3 },
              { "widget": "text", "style": "muted", "text": "{{ data.count }} items" },
              { "widget": "alert", "severity": "danger", "text": "Stop" },
              { "widget": "badge", "color": "success", "text": "ok" },
              { "widget": "divider" } ]
            """, """{ "count": 2 }""");

        html.Should().StartWith("<div class=\"middt-ui\" data-layout=\"test\">");
        html.Should().Contain("<h3 class=\"middt-ui-heading\">Hello alice</h3>");
        html.Should().Contain("<p class=\"middt-ui-text middt-ui-text-muted\">2 items</p>");
        html.Should().Contain("<div class=\"middt-ui-alert middt-ui-alert-danger\" role=\"alert\">Stop</div>");
        html.Should().Contain("<span class=\"middt-ui-badge middt-ui-badge-success\">ok</span>");
        html.Should().EndWith("<hr class=\"middt-ui-divider\"></div>");
    }

    [Fact]
    public async Task Data_is_html_encoded_in_text_and_attributes()
    {
        var html = await RenderAsync("""
            [ { "widget": "text", "text": "{{ data.name }}" },
              { "widget": "link", "text": "{{ data.name }}", "href": "/ui/x?q={{ data.q }}" } ]
            """, """{ "name": "<script>alert(1)</script>", "q": "\"><img src=x onerror=alert(1)>" }""");

        html.Should().NotContain("<script>").And.NotContain("<img");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("href=\"/ui/x?q=&quot;&gt;&lt;img src=x onerror=alert(1)&gt;\"");
    }

    [Fact]
    public async Task VisibleWhen_hides_nodes_and_their_children()
    {
        var html = await RenderAsync("""
            [ { "widget": "text", "text": "shown", "visibleWhen": "data.amount > 10" },
              { "widget": "section", "visibleWhen": "data.amount > 1000", "children": [ { "widget": "text", "text": "hidden" } ] },
              { "widget": "text", "text": "missing-member", "visibleWhen": "data.missing.x == 1" } ]
            """, """{ "amount": 50 }""");

        html.Should().Contain("shown").And.NotContain("hidden").And.NotContain("missing-member").And.NotContain("middt-ui-section");
    }

    [Fact]
    public async Task Fields_render_labels_formatted_values_and_skip_hidden_items()
    {
        var html = await RenderAsync("""
            [ { "widget": "fields", "items": [
                { "label": "Amount", "value": "{{ data.amount }}", "format": "N2" },
                { "label": "Paid", "value": "{{ data.paid }}" },
                { "label": "Missing", "value": "{{ data.nope.deeper }}" },
                { "label": "Secret", "value": "x", "visibleWhen": "data.amount < 0" } ] } ]
            """, """{ "amount": 1234.5, "paid": false }""");

        UiRenderInput.VisibleText(html).Should().Be("Amount 1,234.50 Paid Hayır Missing");
        html.Should().Contain("<dd>1,234.50</dd>").And.Contain("<dd>Hay&#x131;r</dd>").And.NotContain("Secret").And.NotContain("⚠");
    }

    [Fact]
    public async Task List_repeats_its_children_for_each_row()
    {
        var html = await RenderAsync("""
            [ { "widget": "list", "source": "data.items", "children": [ { "widget": "text", "text": "{{ row.name }} ({{ data.owner }})" } ] } ]
            """, """{ "owner": "ada", "items": [ { "name": "a" }, { "name": "b" }, { "name": "<c>" } ] }""");

        html.Split("<li>").Should().HaveCount(4);
        html.Should().Contain("a (ada)").And.Contain("b (ada)").And.Contain("&lt;c&gt; (ada)");
    }

    [Fact]
    public async Task List_shows_the_empty_text_for_empty_missing_or_failing_sources()
    {
        (await RenderAsync("""[ { "widget": "list", "source": "data.items", "empty": "Nothing", "children": [ { "widget": "divider" } ] } ]""", """{ "items": [] }"""))
            .Should().Contain("Nothing").And.NotContain("<ul");
        (await RenderAsync("""[ { "widget": "list", "source": "data.items", "children": [ { "widget": "divider" } ] } ]"""))
            .Should().Contain("Kay&#x131;t yok.");
        (await RenderAsync("""[ { "widget": "list", "source": "data.a / data.b", "children": [ { "widget": "divider" } ] } ]""", """{ "a": 1, "b": 0 }"""))
            .Should().Contain("Kay&#x131;t yok.", "division by zero yields null");
    }

    [Fact]
    public async Task Section_and_columns_render_their_children()
    {
        var html = await RenderAsync("""
            [ { "widget": "columns", "widths": [ "2", 1, "x" ], "children": [
                { "widget": "section", "id": "left", "title": "Left {{ data.n }}", "children": [ { "widget": "text", "text": "L" } ] },
                { "widget": "text", "text": "M" },
                { "widget": "text", "text": "R" } ] } ]
            """, """{ "n": 1 }""");

        html.Should().Contain("style=\"flex: 2 1 0\"");
        Regex.Matches(html, "style=\"flex: 1 1 0\"").Should().HaveCount(2);
        html.Should().Contain("<section class=\"middt-ui-section\" aria-labelledby=\"ui-left-title\">");
        html.Should().Contain("<h3 class=\"middt-ui-section-title\" id=\"ui-left-title\">Left 1</h3>");
    }

    [Fact]
    public async Task Section_title_ids_are_unique_without_node_ids()
    {
        var html = await RenderAsync("""
            [ { "widget": "section", "title": "A", "children": [ { "widget": "divider" } ] },
              { "widget": "section", "title": "B", "children": [ { "widget": "divider" } ] },
              { "widget": "section", "children": [ { "widget": "divider" } ] } ]
            """);

        var ids = Regex.Matches(html, " id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        Regex.Matches(html, "aria-labelledby").Should().HaveCount(2, "sections without title have no aria-labelledby");
    }

    [Fact]
    public async Task Interactive_widgets_render_a_placeholder_in_static_rendering()
    {
        var html = await RenderAsync("""
            [ { "widget": "grid", "entity": "Order", "title": "Orders of {{ data.customer }}", "columns": [ { "field": "number" } ] },
              { "widget": "record", "entity": "Order", "recordId": "1", "children": [ { "widget": "text", "text": "inner" } ] } ]
            """, """{ "customer": "<Ada>" }""");

        html.Should().Contain("<div class=\"middt-ui-placeholder\">Orders of &lt;Ada&gt;</div>");
        html.Should().Contain($"<div class=\"middt-ui-placeholder\">{UiWidgets.Record.Description.Replace("'", "&#x27;", StringComparison.Ordinal)}</div>");
        html.Should().NotContain("inner");
    }

    [Fact]
    public async Task Widgets_without_a_component_report_it_in_interactive_hosts()
    {
        var html = await RenderAsync("""[ { "widget": "grid", "entity": "Order", "columns": [] } ]""", interactive: true, widgets: new UiBlazorWidgets(UiWidgetCatalog.CreateDefault()));

        WebUtility.HtmlDecode(html).Should().Contain("'grid' bileşeni bu uygulamada kayıtlı değil.");
    }

    [Fact]
    public async Task Unknown_widgets_render_an_error_message_with_the_encoded_name()
    {
        var html = await RenderAsync("""[ { "widget": "<marquee>" } ]""");

        html.Should().Contain("<div class=\"middt-ui-error\" role=\"alert\">").And.Contain("&lt;marquee&gt;</div>").And.NotContain("<marquee>");
        WebUtility.HtmlDecode(html).Should().Contain("Bilinmeyen bileşen: <marquee>");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.com")]
    [InlineData("{{ data.url }}")]
    public async Task Unsafe_links_render_as_text(string href)
    {
        var html = await RenderAsync($$"""[ { "widget": "link", "text": "Go", "href": "{{href}}" } ]""", """{ "url": "javascript:alert(1)" }""");

        html.Should().Be("<div class=\"middt-ui\" data-layout=\"test\"><span class=\"middt-ui-link-disabled\">Go</span></div>");
    }

    [Fact]
    public async Task Safe_links_render_as_anchors()
    {
        (await RenderAsync("""[ { "widget": "link", "text": "Open", "href": "/ui/order?id={{ query.id }}", "style": "button" } ]""", query: """{ "id": "7" }"""))
            .Should().Contain("<a class=\"middt-ui-button\" href=\"/ui/order?id=7\">Open</a>");
    }

    [Fact]
    public async Task Custom_widgets_render_through_the_registry()
    {
        var widgets = UiBlazorWidgets.CreateDefault()
            .Add<StarsWidget>(new UiWidgetSpec("stars", "Rating") { Props = [new("count", UiPropType.Integer), new("label", UiPropType.Template)] });

        var html = await RenderAsync("""[ { "widget": "stars", "count": 3, "label": "{{ data.n }}" } ]""", """{ "n": "good" }""", widgets: widgets);

        html.Should().Contain("<span class=\"stars\">*** good</span>");
    }

    [Fact]
    public async Task Sample_order_escalated_layout_has_the_same_visible_text_as_the_static_renderer()
    {
        var layout = UiStaticRenderingTests.OrderEscalated();
        var options = new MiddtUiOptions();
        var staticHtml = new UiHtmlRenderer(options.Widgets, Options.Create(options))
            .Render(layout, UiStaticRenderingTests.OrderEscalatedVariables(), UiRenderInput.User, CultureInfo.InvariantCulture);

        var blazorHtml = await RenderAsync(layout, UiStaticRenderingTests.OrderEscalatedVariables());

        var text = UiRenderInput.VisibleText(staticHtml);
        text.Should().Contain("Sipariş #42").And.Contain("Ada <Ltd>").And.Contain("Skor 72");
        UiRenderInput.VisibleText(blazorHtml).Should().Be(text);
    }

    [Fact]
    public async Task Static_and_blazor_renderers_produce_the_same_visible_text_for_every_display_widget()
    {
        var layout = UiRenderInput.Layout("""
            [ { "widget": "heading", "text": "H {{ user.Name }}" },
              { "widget": "text", "text": "T {{ data.n }}" },
              { "widget": "alert", "severity": "warning", "text": "A" },
              { "widget": "badge", "text": "B" },
              { "widget": "divider" },
              { "widget": "link", "text": "L", "href": "/x" },
              { "widget": "link", "text": "U", "href": "javascript:x" },
              { "widget": "columns", "children": [ { "widget": "section", "title": "S", "children": [ { "widget": "fields", "items": [ { "label": "F", "value": "{{ data.n }}", "format": "N1" } ] } ] } ] },
              { "widget": "list", "source": "data.items", "children": [ { "widget": "text", "text": "{{ row }}" } ] },
              { "widget": "list", "source": "data.none", "children": [ { "widget": "divider" } ] },
              { "widget": "grid", "entity": "A", "columns": [] },
              { "widget": "nope" } ]
            """);
        var variables = UiRenderInput.Variables("""{ "n": 3, "items": [ "x", "y" ] }""");
        var options = new MiddtUiOptions();

        var staticHtml = new UiHtmlRenderer(options.Widgets, Options.Create(options)).Render(layout, variables, UiRenderInput.User, CultureInfo.InvariantCulture);
        var blazorHtml = await RenderAsync(layout, variables);

        UiRenderInput.VisibleText(blazorHtml).Should().Be(UiRenderInput.VisibleText(staticHtml));
    }

    [Fact]
    public void Use_requires_a_spec()
    {
        var widgets = new UiBlazorWidgets(UiWidgetCatalog.CreateDefault());

        FluentActions.Invoking(() => widgets.Use<StarsWidget>("stars")).Should().Throw<InvalidOperationException>().WithMessage("*stars*");
        widgets.Find("stars").Should().BeNull();
    }

    [Fact]
    public void Use_requires_a_UiWidgetBase_component()
    {
        var widgets = new UiBlazorWidgets(UiWidgetCatalog.CreateDefault());

        FluentActions.Invoking(() => widgets.Use("heading", typeof(UiLayoutView))).Should().Throw<ArgumentException>();
        widgets.Find("heading").Should().BeNull();
    }

    [Fact]
    public void Add_registers_the_spec_and_the_component()
    {
        var catalog = UiWidgetCatalog.CreateDefault();
        var spec = new UiWidgetSpec("stars", "Rating");

        var widgets = new UiBlazorWidgets(catalog).Add<StarsWidget>(spec);

        catalog.Find("stars").Should().BeSameAs(spec);
        widgets.Catalog.Should().BeSameAs(catalog);
        widgets.Find("stars").Should().Be<StarsWidget>();
    }

    [Fact]
    public void Default_registry_maps_every_built_in_widget()
    {
        var widgets = UiBlazorWidgets.CreateDefault();

        foreach (var spec in UiWidgets.All)
        {
            widgets.Find(spec.Name).Should().NotBeNull(spec.Name);
        }

        widgets.Find("heading").Should().Be<HeadingWidget>();
        widgets.Find("unknown").Should().BeNull();
    }
}
