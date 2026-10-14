using System.Globalization;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Ui;

public sealed class UiRenderContextTests
{
    private static UiRenderContext Context(string data = """{ "amount": 7500.5, "count": 3, "zero": 0, "name": "Ada", "active": true, "date": "2026-01-02T10:30:00" }""", ExpressionUser? user = null) =>
        new(UiWidgetCatalog.CreateDefault(), user ?? new ExpressionUser { Name = "alice" }, new Dictionary<string, JsonNode?> { [UiExpressions.Data] = JsonNode.Parse(data) })
        {
            Culture = CultureInfo.InvariantCulture,
        };

    [Fact]
    public void Text_returns_plain_text_unchanged_and_empty_for_null()
    {
        Context().Text("Hello").Should().Be("Hello");
        Context().Text(null).Should().BeEmpty();
        Context().Text(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Text_renders_templates_with_the_user_and_data()
    {
        Context().Text("Hi {{ user.Name }}, {{ data.name }} has {{ data.count }}").Should().Be("Hi alice, Ada has 3");
    }

    [Fact]
    public void Text_formats_numbers_and_dates_with_the_context_culture()
    {
        Context().Text("{{ data.amount }}", "N2").Should().Be("7,500.50");
        Context().Text("{{ data.count }}", "N0").Should().Be("3");
        Context().Text("{{ data.date }}", "d").Should().Be("01/02/2026");
        Context().Text("{{ data.name }}", "d").Should().Be("Ada", "non-date strings ignore the format");
        new UiRenderContext(UiWidgetCatalog.CreateDefault(), new ExpressionUser(), new Dictionary<string, JsonNode?> { ["data"] = JsonNode.Parse("""{ "amount": 1234.5 }""") }) { Culture = new CultureInfo("tr-TR") }
            .Text("{{ data.amount }}", "N2").Should().Be("1.234,50");
    }

    [Fact]
    public void Format_renders_booleans_in_turkish_and_objects_as_json()
    {
        var context = Context();

        context.Text("{{ data.active }}").Should().Be("Evet");
        context.Format(JsonValue.Create(false)).Should().Be("Hayır");
        context.Format(null).Should().BeEmpty();
        context.Format(JsonNode.Parse("""{ "a": 1 }""")).Should().Be("""{"a":1}""");
    }

    [Theory]
    [InlineData("{{ data.amount + }}")]
    [InlineData("{{ unknown.value }}")]
    [InlineData("{{ data.name.GetType() }}")]
    [InlineData("Total {{ data.amount * }}")]
    public void Syntax_errors_are_returned_as_warning_text(string template)
    {
        var text = Context().Text(template);

        text.Should().StartWith("⚠ ");
    }

    [Theory]
    [InlineData("{{ data.missing.name }}", "")]
    [InlineData("{{ data.count / data.zero }}", "")]
    [InlineData("{{ data.name * 2 }}", "")]
    [InlineData("x{{ data.missing.name }}y", "xy")]
    [InlineData("{{ data.missing ?? \"n/a\" }}", "n/a")]
    [InlineData("{{ data.count * 2 + 1 }}", "7")]
    [InlineData("{{ data.name + data.count }}", "Ada3")]
    [InlineData("{{ data.name.Length }}", "3")]
    [InlineData("{{ data.amount > 1000 ? \"big\" : \"small\" }}", "big")]
    public void Runtime_problems_yield_empty_values_instead_of_errors(string template, string expected) =>
        Context().Text(template).Should().Be(expected);

    [Fact]
    public void IsVisible_evaluates_conditions_with_truthiness_and_hides_on_errors()
    {
        var context = Context();

        context.IsVisible(new UiNode { Widget = "divider" }).Should().BeTrue();
        context.IsVisible("data.amount > 1000").Should().BeTrue();
        context.IsVisible("data.amount > 10000").Should().BeFalse();
        context.IsVisible("data.missing.value > 1").Should().BeFalse("null compares as false");
        context.IsVisible("data.amount >").Should().BeFalse("parse errors hide the node");
        context.IsVisible("data.name").Should().BeTrue("non-empty text is truthy");
        context.IsVisible("data.zero").Should().BeFalse("0 is falsy");
        context.IsVisible("data.missing").Should().BeFalse("null is falsy");
        context.IsVisible("data.count / data.zero > 1").Should().BeFalse("division by zero yields null");
        context.IsVisible("  ").Should().BeTrue();
    }

    [Fact]
    public void Format_ignores_unsupported_formats()
    {
        Context().Text("{{ data.amount }}", "C2").Should().Be("7500.5");
        Context().Text("{{ data.amount }}", "F1").Should().Be("7500.5");
        Context().Text("{{ data.count * 1.5 }}", "N1").Should().Be("4.5", "computed doubles are formatted too");
    }

    [Fact]
    public void IsVisible_can_check_roles()
    {
        Context(user: new ExpressionUser { Roles = ["manager"] }).IsVisible("user.IsInRole(\"manager\")").Should().BeTrue();
        Context().IsVisible("user.IsInRole(\"manager\")").Should().BeFalse();
    }

    [Fact]
    public void With_returns_a_copy_and_does_not_change_the_parent()
    {
        var parent = Context();

        var child = parent.With(UiExpressions.Row, JsonNode.Parse("""{ "name": "row-1" }"""));
        var replaced = parent.With(UiExpressions.Data, JsonNode.Parse("""{ "name": "Grace" }"""));

        child.Text("{{ row.name }} / {{ data.name }}").Should().Be("row-1 / Ada");
        replaced.Text("{{ data.name }}").Should().Be("Grace");
        parent.Variables.Should().NotContainKey(UiExpressions.Row);
        parent.Text("{{ data.name }}").Should().Be("Ada");
        child.Culture.Should().BeSameAs(parent.Culture);
        child.User.Should().BeSameAs(parent.User);
    }

    [Fact]
    public void IdFor_uses_the_prefix_and_node_id_or_widget()
    {
        var context = new UiRenderContext(UiWidgetCatalog.CreateDefault(), new ExpressionUser()) { IdPrefix = "p" };

        context.IdFor(new UiNode { Widget = "section" }).Should().Be("p-section");
        context.IdFor(new UiNode { Widget = "section", Id = "main" }, "title").Should().Be("p-main-title");
    }

    [Theory]
    [InlineData("/ui/orders?id=1", true)]
    [InlineData("/", true)]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JavaScript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("relative/path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafe_allows_local_paths_and_http_urls_only(string? url, bool expected) =>
        UiUrl.IsSafe(url).Should().Be(expected);

    [Theory]
    [InlineData("/ui/orders", true)]
    [InlineData("/", true)]
    [InlineData("https://example.com", false)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void IsLocal_allows_local_paths_only(string? url, bool expected) =>
        UiUrl.IsLocal(url).Should().Be(expected);
}
