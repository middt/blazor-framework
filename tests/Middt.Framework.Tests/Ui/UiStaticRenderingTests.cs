using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Server;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Ui;

/// <summary>Shared input for the static (<see cref="UiHtmlRenderer"/>) and Blazor rendering tests.</summary>
internal static partial class UiRenderInput
{
    public static UiLayoutDocument Layout(string body) => UiJson.Parse($$"""{ "key": "test", "body": {{body}} }""");

    public static Dictionary<string, JsonNode?> Variables(string? data = null, string? query = null)
    {
        var variables = new Dictionary<string, JsonNode?> { [UiExpressions.Data] = data is null ? new JsonObject() : JsonNode.Parse(data) };
        if (query is not null)
        {
            variables[UiExpressions.Query] = JsonNode.Parse(query);
        }

        return variables;
    }

    public static ExpressionUser User { get; } = new() { Name = "alice" };

    /// <summary>Visible text of rendered HTML: tags removed, entities decoded, whitespace collapsed.</summary>
    public static string VisibleText(string html) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(html, " ")), " ").Trim();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

/// <summary>The plain C# static HTML renderer of the API (workflow views, e-mails, <c>GET api/ui/layouts/{key}/html</c>).</summary>
public sealed class UiStaticRenderingTests
{
    private static string Render(string body, string? data = null, string? query = null, MiddtUiOptions? options = null)
    {
        options ??= new MiddtUiOptions();
        var renderer = new UiHtmlRenderer(options.Widgets, Options.Create(options));
        return renderer.Render(UiRenderInput.Layout(body), UiRenderInput.Variables(data, query), UiRenderInput.User, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Display_widgets_render_their_markup()
    {
        var html = Render("""
            [ { "widget": "heading", "text": "Hello {{ user.Name }}", "level": 3 },
              { "widget": "text", "style": "muted", "text": "{{ data.count }} items" },
              { "widget": "alert", "severity": "danger", "text": "Stop" },
              { "widget": "alert", "text": "Info" },
              { "widget": "badge", "color": "success", "text": "ok" },
              { "widget": "divider" } ]
            """, """{ "count": 2 }""");

        html.Should().StartWith("<div class=\"middt-ui\" data-layout=\"test\">");
        html.Should().Contain("<h3 class=\"middt-ui-heading\">Hello alice</h3>");
        html.Should().Contain("<p class=\"middt-ui-text middt-ui-text-muted\">2 items</p>");
        html.Should().Contain("<div class=\"middt-ui-alert middt-ui-alert-danger\" role=\"alert\">Stop</div>");
        html.Should().Contain("<div class=\"middt-ui-alert middt-ui-alert-info\" role=\"status\">Info</div>");
        html.Should().Contain("<span class=\"middt-ui-badge middt-ui-badge-success\">ok</span>");
        html.Should().EndWith("<hr class=\"middt-ui-divider\" /></div>");
    }

    [Fact]
    public void Heading_level_is_clamped()
    {
        Render("""[ { "widget": "heading", "text": "a", "level": 9 }, { "widget": "heading", "text": "b", "level": 0 } ]""")
            .Should().Contain("<h6 class=\"middt-ui-heading\">a</h6>").And.Contain("<h1 class=\"middt-ui-heading\">b</h1>");
    }

    [Fact]
    public void Data_is_html_encoded_in_text_and_attributes()
    {
        var html = Render("""
            [ { "widget": "text", "text": "{{ data.name }}" },
              { "widget": "heading", "text": "Hi {{ data.name }}" },
              { "widget": "link", "text": "{{ data.name }}", "href": "/ui/x?q={{ data.q }}" } ]
            """, """{ "name": "<script>alert(1)</script>", "q": "\"><img src=x onerror=alert(1)>" }""");

        html.Should().NotContain("<script>").And.NotContain("<img");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("href=\"/ui/x?q=&quot;&gt;&lt;img src=x onerror=alert(1)&gt;\"");
    }

    [Fact]
    public void Layout_key_is_encoded_in_the_data_attribute()
    {
        var options = new MiddtUiOptions();
        var html = new UiHtmlRenderer(options.Widgets, Options.Create(options))
            .Render(new UiLayoutDocument { Key = "a\"b", Body = [] }, new Dictionary<string, JsonNode?>(), UiRenderInput.User);

        html.Should().Be("<div class=\"middt-ui\" data-layout=\"a&quot;b\"></div>");
    }

    [Fact]
    public void VisibleWhen_hides_nodes_and_their_children()
    {
        var html = Render("""
            [ { "widget": "text", "text": "shown", "visibleWhen": "data.amount > 10" },
              { "widget": "section", "visibleWhen": "data.amount > 1000", "children": [ { "widget": "text", "text": "hidden" } ] },
              { "widget": "text", "text": "missing-member", "visibleWhen": "data.missing.x == 1" },
              { "widget": "text", "text": "syntax-error", "visibleWhen": "data.amount >" },
              { "widget": "text", "text": "truthy", "visibleWhen": "data.name" } ]
            """, """{ "amount": 50, "name": "Ada" }""");

        html.Should().Contain("shown").And.Contain("truthy");
        html.Should().NotContain("hidden").And.NotContain("missing-member").And.NotContain("syntax-error").And.NotContain("middt-ui-section");
    }

    [Fact]
    public void Fields_render_labels_formatted_values_and_skip_hidden_items()
    {
        var html = Render("""
            [ { "widget": "fields", "items": [
                { "label": "Amount", "value": "{{ data.amount }}", "format": "N2" },
                { "label": "Fixed", "value": "{{ data.amount }}", "format": "F0" },
                { "label": "Date", "value": "{{ data.date }}", "format": "d" },
                { "label": "Paid", "value": "{{ data.paid }}" },
                { "label": "Missing", "value": "{{ data.nope.deeper }}" },
                { "label": "<b>", "value": "x" },
                { "label": "Secret", "value": "x", "visibleWhen": "data.amount < 0" } ] } ]
            """, """{ "amount": 1234.5, "paid": false, "date": "2026-01-02T10:30:00" }""");

        html.Should().StartWith("<div class=\"middt-ui\" data-layout=\"test\"><dl class=\"middt-ui-fields\">");
        html.Should().Contain("<div class=\"middt-ui-field\"><dt>Amount</dt><dd>1,234.50</dd></div>");
        html.Should().Contain("<dt>Fixed</dt><dd>1235</dd>");
        html.Should().Contain("<dt>Date</dt><dd>01/02/2026</dd>");
        html.Should().Contain("<dd>Hay&#x131;r</dd>");
        html.Should().Contain("<dt>Missing</dt><dd></dd>", "missing members yield null, not an error");
        html.Should().Contain("<dt>&lt;b&gt;</dt>");
        html.Should().NotContain("Secret");
    }

    [Fact]
    public void List_repeats_its_children_for_each_row()
    {
        var html = Render("""
            [ { "widget": "list", "source": "data.items", "children": [ { "widget": "text", "text": "{{ row.name }} ({{ data.owner }})" } ] } ]
            """, """{ "owner": "ada", "items": [ { "name": "a" }, { "name": "b" }, { "name": "<c>" } ] }""");

        html.Should().Contain("<ul class=\"middt-ui-list\">");
        html.Split("<li>").Should().HaveCount(4);
        html.Should().Contain("a (ada)").And.Contain("b (ada)").And.Contain("&lt;c&gt; (ada)");
    }

    [Theory]
    [InlineData("data.items", """{ "items": [] }""")]
    [InlineData("data.items", null)]
    [InlineData("data.a / data.b", """{ "a": 1, "b": 0 }""")]
    [InlineData("data.items", """{ "items": "text" }""")]
    public void List_shows_the_empty_text_for_empty_missing_or_non_array_sources(string source, string? data)
    {
        var body = $$"""[ { "widget": "list", "source": "{{source}}", "children": [ { "widget": "divider" } ] } ]""";

        var html = Render(body, data);

        html.Should().Contain("<p class=\"middt-ui-text middt-ui-text-muted\">Kay&#x131;t yok.</p>").And.NotContain("<ul");
    }

    [Fact]
    public void List_uses_the_configured_empty_text()
    {
        Render("""[ { "widget": "list", "source": "data.items", "empty": "<Nothing>", "children": [ { "widget": "divider" } ] } ]""", """{ "items": [] }""")
            .Should().Contain("&lt;Nothing&gt;");
    }

    [Fact]
    public void Section_and_columns_render_their_children()
    {
        var html = Render("""
            [ { "widget": "columns", "widths": [ "2", 1, "x" ], "children": [
                { "widget": "section", "id": "left", "title": "Left {{ data.n }}", "children": [ { "widget": "text", "text": "L" } ] },
                { "widget": "text", "text": "M" },
                { "widget": "text", "text": "R" } ] } ]
            """, """{ "n": 1 }""");

        html.Should().Contain("<div class=\"middt-ui-columns\"><div class=\"middt-ui-column\" style=\"flex: 2 1 0\">");
        Regex.Matches(html, "style=\"flex: 1 1 0\"").Should().HaveCount(2, "the numeric width 1 and the invalid width 'x' both become 1");
        html.Should().Contain("<section class=\"middt-ui-section\" aria-labelledby=\"ui-left-title\">");
        html.Should().Contain("<h3 class=\"middt-ui-section-title\" id=\"ui-left-title\">Left 1</h3>");
    }

    [Fact]
    public void Section_title_ids_are_unique_without_node_ids()
    {
        var html = Render("""
            [ { "widget": "section", "title": "A", "children": [ { "widget": "divider" } ] },
              { "widget": "section", "title": "B", "children": [ { "widget": "divider" } ] },
              { "widget": "section", "children": [ { "widget": "divider" } ] } ]
            """);

        var ids = Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        foreach (var id in ids)
        {
            html.Should().Contain($"aria-labelledby=\"{id}\"");
        }

        html.Should().Contain("<section class=\"middt-ui-section\"><hr", "sections without title have no aria-labelledby");
    }

    [Fact]
    public void Interactive_widgets_render_a_placeholder()
    {
        var html = Render("""
            [ { "widget": "grid", "entity": "Order", "title": "Orders of {{ data.customer }}", "columns": [ { "field": "number" } ] },
              { "widget": "record", "entity": "Order", "recordId": "1", "children": [ { "widget": "text", "text": "inner" } ] } ]
            """, """{ "customer": "<Ada>" }""");

        html.Should().Contain("<div class=\"middt-ui-placeholder\">Orders of &lt;Ada&gt;</div>");
        html.Should().Contain($"<div class=\"middt-ui-placeholder\">{UiWidgets.Record.Description.Replace("'", "&#x27;", StringComparison.Ordinal)}</div>");
        html.Should().NotContain("inner");
    }

    [Fact]
    public void Unknown_widgets_render_an_error_message_with_the_encoded_name()
    {
        Render("""[ { "widget": "<marquee>" } ]""")
            .Should().Contain("<div class=\"middt-ui-error\" role=\"alert\">Bilinmeyen bile&#x15F;en: &lt;marquee&gt;</div>");
    }

    [Fact]
    public void Custom_specs_without_html_output_render_an_error()
    {
        var options = new MiddtUiOptions().AddWidget(new UiWidgetSpec("chart", "Chart"));

        WebUtility.HtmlDecode(Render("""[ { "widget": "chart" } ]""", options: options))
            .Should().Contain("'chart' bileşeni için HTML çıktısı tanımlı değil.");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.com")]
    [InlineData("{{ data.url }}")]
    public void Unsafe_links_render_as_text(string href)
    {
        var html = Render($$"""[ { "widget": "link", "text": "Go", "href": "{{href}}" } ]""", """{ "url": "javascript:alert(1)" }""");

        html.Should().Be("<div class=\"middt-ui\" data-layout=\"test\"><span class=\"middt-ui-link-disabled\">Go</span></div>");
    }

    [Fact]
    public void Safe_links_render_as_anchors()
    {
        Render("""[ { "widget": "link", "text": "Open", "href": "/ui/order?id={{ query.id }}", "style": "button" } ]""", query: """{ "id": "7" }""")
            .Should().Contain("<a class=\"middt-ui-button\" href=\"/ui/order?id=7\">Open</a>");
        Render("""[ { "widget": "link", "text": "Docs", "href": "https://example.com/a" } ]""")
            .Should().Contain("<a class=\"middt-ui-link\" href=\"https://example.com/a\">Docs</a>");
    }

    [Fact]
    public void Custom_html_widgets_render_encoded_output_and_children()
    {
        var options = new MiddtUiOptions()
            .AddWidget(
                new UiWidgetSpec("stars", "Rating") { Props = [new("count", UiPropType.Integer), new("label", UiPropType.Template)] },
                (node, context, html, _) => html.Raw("<span class=\"stars\">")
                    .Text(new string('*', Math.Clamp(node.IntProp("count", 1), 1, 5)) + " " + context.Text(node.StringProp("label")))
                    .Raw("</span>"))
            .AddWidget(
                new UiWidgetSpec("box", "Box") { Children = UiChildren.Required },
                (node, context, html, children) =>
                {
                    html.Raw("<div class=\"box\">");
                    children(node.Children, context.With(UiExpressions.Row, new JsonObject { ["n"] = "inner" }));
                    html.Raw("</div>");
                });

        var html = Render("""
            [ { "widget": "stars", "count": 3, "label": "{{ data.n }}" },
              { "widget": "box", "children": [ { "widget": "text", "text": "{{ row.n }}" } ] } ]
            """, """{ "n": "<good>" }""", options: options);

        html.Should().Contain("<span class=\"stars\">*** &lt;good&gt;</span>");
        html.Should().Contain("<div class=\"box\"><p class=\"middt-ui-text middt-ui-text-normal\">inner</p></div>");
    }

    [Fact]
    public void Html_output_of_built_in_widgets_can_be_replaced()
    {
        var options = new MiddtUiOptions().AddHtmlWidget("divider", (_, _, html, _) => html.Raw("<hr class=\"custom\" />"));

        Render("""[ { "widget": "divider" } ]""", options: options).Should().Contain("<hr class=\"custom\" />").And.NotContain("middt-ui-divider");
    }

    [Fact]
    public void Renders_layouts_with_query_data_and_user_variables()
    {
        var options = new MiddtUiOptions();
        var html = new UiHtmlRenderer(options.Widgets, Options.Create(options)).Render(
            UiRenderInput.Layout("""[ { "widget": "heading", "text": "{{ query.name }} / {{ data.x }} / {{ user.Name }}" } ]"""),
            new Dictionary<string, JsonNode?> { [UiExpressions.Query] = new JsonObject { ["name"] = "<b>q</b>" }, [UiExpressions.Data] = new JsonObject { ["x"] = 1 } },
            new ExpressionUser { Name = "bob" });

        html.Should().Be("<div class=\"middt-ui\" data-layout=\"test\"><h2 class=\"middt-ui-heading\">&lt;b&gt;q&lt;/b&gt; / 1 / bob</h2></div>");
    }

    [Fact]
    public void Uses_the_given_culture_for_formats()
    {
        var options = new MiddtUiOptions();
        var html = new UiHtmlRenderer(options.Widgets, Options.Create(options)).Render(
            UiRenderInput.Layout("""[ { "widget": "fields", "items": [ { "label": "a", "value": "{{ data.x }}", "format": "N2" } ] } ]"""),
            UiRenderInput.Variables("""{ "x": 1234.5 }"""), UiRenderInput.User, new CultureInfo("tr-TR"));

        html.Should().Contain("<dd>1.234,50</dd>");
    }

    internal static Dictionary<string, JsonNode?> OrderEscalatedVariables() => new()
    {
        [UiExpressions.Data] = JsonNode.Parse("""{ "orderId": 42, "customerName": "Ada <Ltd>", "amount": 25000, "requestedBy": "bob", "requestedAt": "2026-03-04T05:06:00", "credit": { "score": 72 } }"""),
        [UiExpressions.Instance] = new JsonObject { ["stateTitle"] = "Eskale edildi", ["workflow"] = "order-approval" },
    };

    internal static UiLayoutDocument OrderEscalated() =>
        UiJson.Parse(File.ReadAllText(Path.Combine(UiSampleLayoutTests.SampleDirectory("Layouts"), "order-escalated.json")));

    [Fact]
    public void Sample_order_escalated_layout_renders_instance_data()
    {
        var options = new MiddtUiOptions();
        var html = new UiHtmlRenderer(options.Widgets, Options.Create(options))
            .Render(OrderEscalated(), OrderEscalatedVariables(), UiRenderInput.User, CultureInfo.InvariantCulture);
        var text = WebUtility.HtmlDecode(html);

        text.Should().Contain("Ada <Ltd> siparişi süresi içinde karara bağlanmadığı için eskale edildi.");
        html.Should().Contain("Ada &lt;Ltd&gt;");
        html.Should().Contain("<dd>25,000.00</dd>");
        html.Should().Contain("Skor 72").And.Contain("middt-ui-badge-success").And.NotContain("middt-ui-badge-warning").And.NotContain("middt-ui-badge-danger");
        text.Should().Contain("Yüksek tutar").And.Contain("Durum: Eskale edildi · Akış: order-approval");
        text.Should().NotContain("Firma bilgileri").And.NotContain("⚠").And.NotContain("Not:").And.NotContain("Ürün");
    }
}
