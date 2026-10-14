using System.Security.Claims;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;

namespace Middt.Framework.Tests.Ui;

public sealed class ExpressionEvaluatorTests
{
    private static readonly ExpressionEvaluator Evaluator = new(["data", "query"], ["row"]);

    private static ExpressionContext Context(string data = """{ "amount": 7500, "rate": 0.5, "customer": { "name": "Ada" }, "tags": ["a", "b"] }""", ExpressionUser? user = null)
    {
        var context = user is null ? new ExpressionContext() : new ExpressionContext { User = user };
        return context.Set("data", JsonNode.Parse(data));
    }

    [Fact]
    public void Evaluates_member_access_and_comparisons_over_json_variables()
    {
        Evaluator.EvaluateCondition("data.amount > 5000 && data.customer.name == \"Ada\"", Context()).Should().BeTrue();
        Evaluator.Evaluate("data.amount", Context()).Should().Be(7500L, "whole numbers surface as long");
        Evaluator.Evaluate("data.rate * 2", Context()).Should().Be(1.0);
    }

    [Fact]
    public void Missing_members_and_variables_evaluate_to_null()
    {
        Evaluator.Evaluate("data.missing", Context()).Should().BeNull();
        Evaluator.EvaluateCondition("data.missing == null", Context()).Should().BeTrue();
        Evaluator.Evaluate("query.id", Context()).Should().BeNull("unset object variables behave like an empty object");
        Evaluator.EvaluateCondition("data.missing", Context()).Should().BeFalse("null conditions are false");
    }

    [Fact]
    public void Value_variables_accept_primitives()
    {
        var context = new ExpressionContext().Set("row", JsonValue.Create(21)).Set("data", JsonNode.Parse("""{ "a": 1 }"""));

        Evaluator.Evaluate("row", context).Should().Be(21L);
        Evaluator.Evaluate("row", new ExpressionContext().Set("row", JsonNode.Parse("""{ "name": "x" }"""))).Should().BeOfType<JsonDynamic>();
        Evaluator.EvaluateCondition("row != null", context).Should().BeTrue();
    }

    [Fact]
    public void Non_boolean_conditions_are_rejected()
    {
        FluentActions.Invoking(() => Evaluator.EvaluateCondition("data.amount", Context()))
            .Should().Throw<InvalidOperationException>().WithMessage("*expected bool*");
    }

    [Fact]
    public void Single_placeholder_template_keeps_the_value_type()
    {
        var number = Evaluator.Render(JsonValue.Create("{{ data.amount }}"), Context());
        var obj = Evaluator.Render(JsonValue.Create("{{ data.customer }}"), Context());

        number!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        number.GetValue<long>().Should().Be(7500);
        obj.Should().BeOfType<JsonObject>().Which["name"]!.GetValue<string>().Should().Be("Ada");
    }

    [Fact]
    public void Placeholders_inside_text_are_interpolated_with_invariant_culture()
    {
        var text = Evaluator.RenderText("Total {{ data.rate + 1 }} for {{ data.customer.name }}!", Context());

        text.Should().Be("Total 1.5 for Ada!");
    }

    [Fact]
    public void Templates_render_objects_and_arrays_recursively_and_keep_non_strings()
    {
        var template = JsonNode.Parse("""{ "a": "{{ data.amount + 1 }}", "b": ["x {{ data.customer.name }}", 3, true], "c": null }""");

        var rendered = Evaluator.Render(template, Context());

        rendered!.ToJsonString().Should().Be("""{"a":7501,"b":["x Ada",3,true],"c":null}""");
        template!["a"]!.GetValue<string>().Should().Be("{{ data.amount + 1 }}", "the template is not modified");
    }

    [Fact]
    public void RenderText_serializes_objects_and_returns_empty_for_null()
    {
        Evaluator.RenderText("{{ data.customer }}", Context()).Should().Be("""{"name":"Ada"}""");
        Evaluator.RenderText("{{ data.missing }}", Context()).Should().BeEmpty();
        Evaluator.RenderText("plain", Context()).Should().Be("plain");
    }

    [Theory]
    [InlineData("data.amount >")]
    [InlineData("unknown.value")]
    [InlineData("System.IO.File.Exists(\"x\")")]
    [InlineData("typeof(string).Assembly")]
    [InlineData("new System.Net.WebClient()")]
    public void Validate_reports_syntax_errors_and_unreachable_identifiers(string expression) =>
        Evaluator.Validate(expression).Should().NotBeNullOrEmpty();

    [Theory]
    [InlineData("data.amount > 5000")]
    [InlineData("Math.Max(1, 2) == 2")]
    [InlineData("user.IsInRole(\"manager\") || now.Year > 2000")]
    [InlineData("row != null")]
    public void Validate_accepts_valid_expressions(string expression) =>
        Evaluator.Validate(expression).Should().BeNull();

    [Fact]
    public void ValidateTemplate_reports_each_invalid_placeholder()
    {
        var template = JsonNode.Parse("""{ "ok": "{{ data.a }}", "bad": ["{{ data.a + }}", "x {{ nope.b }}"] }""");

        Evaluator.ValidateTemplate(template).Should().HaveCount(2);
        Evaluator.ValidateTemplate(JsonValue.Create("no placeholders")).Should().BeEmpty();
    }

    [Fact]
    public void IsTemplate_detects_placeholders()
    {
        ExpressionEvaluator.IsTemplate("Hello {{ user.Name }}").Should().BeTrue();
        ExpressionEvaluator.IsTemplate("Hello { user }").Should().BeFalse();
        ExpressionEvaluator.IsTemplate(null).Should().BeFalse();
    }

    [Fact]
    public void User_roles_are_checked_and_system_users_pass_every_role()
    {
        var manager = new ExpressionUser { Name = "bob", Roles = ["manager"] };
        var clerk = new ExpressionUser { Name = "eve", Roles = ["clerk"] };

        Evaluator.EvaluateCondition("user.IsInRole(\"manager\")", Context(user: manager)).Should().BeTrue();
        Evaluator.EvaluateCondition("user.IsInRole(\"manager\")", Context(user: clerk)).Should().BeFalse();
        Evaluator.EvaluateCondition("user.IsInRole(\"manager\")", Context()).Should().BeTrue("the default context user is the system");
        Evaluator.Evaluate("user.Name", Context(user: manager)).Should().Be("bob");
    }

    [Fact]
    public void FromPrincipal_maps_authenticated_users()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(ClaimTypes.NameIdentifier, "nid-1"),
            new Claim(ClaimTypes.Sid, "sid-1"),
            new Claim(ClaimTypes.Role, "manager"),
            new Claim(ClaimTypes.Role, "clerk"),
        ], "test"));

        var user = ExpressionUser.FromPrincipal(principal);

        user.Name.Should().Be("alice");
        user.Id.Should().Be("sid-1", "the sid claim wins over the name identifier");
        user.Roles.Should().Equal("manager", "clerk");
        user.IsSystem.Should().BeFalse();
        user.IsInRole("Manager").Should().BeFalse("roles are case sensitive");
    }

    [Fact]
    public void FromPrincipal_returns_anonymous_for_unauthenticated_principals()
    {
        var anonymous = ExpressionUser.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));

        anonymous.Name.Should().Be("anonymous");
        anonymous.Roles.Should().BeEmpty();
        anonymous.IsInRole("admin").Should().BeFalse();
        ExpressionUser.FromPrincipal(null).Name.Should().Be("anonymous");
    }
}
