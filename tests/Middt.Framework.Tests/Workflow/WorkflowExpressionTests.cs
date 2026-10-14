using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Expressions;

namespace Middt.Framework.Tests.Workflow;

public sealed class WorkflowExpressionTests
{
    private static readonly WorkflowExpressionEngine Engine = new();

    private static readonly DateTimeOffset FixedNow = new(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

    private static ExpressionScope Scope(string data = "{}", string? input = null, string? child = null, WorkflowUser? user = null) => new()
    {
        Data = JsonNode.Parse(data)!.AsObject(),
        Input = input is null ? null : JsonNode.Parse(input)!.AsObject(),
        Child = child is null ? null : JsonNode.Parse(child)!.AsObject(),
        User = user ?? new WorkflowUser { Name = "alice", Id = "42", Roles = ["manager"] },
        Now = FixedNow,
        Instance = new JsonObject { ["id"] = "i-1", ["key"] = "order-7", ["state"] = "review", ["workflow"] = "orders" },
    };

    private const string Order = """
        { "amount": 7500.5, "count": 3, "name": "Alice Smith", "vip": true,
          "customer": { "address": { "city": "Ankara", "zip": "06100" }, "tags": [ "a", "b" ] } }
        """;

    [Theory]
    [InlineData("data.amount > 5000", true)]
    [InlineData("data.amount > 10000", false)]
    [InlineData("data.count == 3", true)]
    [InlineData("data.count * 2 == 6", true)]
    [InlineData("data.vip && data.amount >= 7500.5", true)]
    [InlineData("data.amount * 0.5 == 3750.25", true)]
    [InlineData("data.count * 2.5 == 7.5", true)]
    [InlineData("data.customer.address.city == \"Ankara\"", true)]
    [InlineData("data.customer.address.zip.StartsWith(\"06\")", true)]
    [InlineData("data.name.Length == 11", true)]
    [InlineData("data.name.Contains(\"Smith\") || false", true)]
    [InlineData("data.name + \"!\" == \"Alice Smith!\"", true)]
    [InlineData("data.missing == null", true)]
    [InlineData("data.customer.missing == null", true)]
    [InlineData("data.customer[\"address\"].city == \"Ankara\"", true)]
    [InlineData("Math.Max(data.count, 10) == 10", true)]
    public void Conditions_evaluate_nested_data_strings_and_numbers(string expression, bool expected)
    {
        Engine.EvaluateCondition(expression, Scope(Order)).Should().Be(expected);
    }

    [Fact]
    public void Missing_member_as_condition_is_false()
    {
        Engine.EvaluateCondition("data.notThere", Scope(Order)).Should().BeFalse();
    }

    [Fact]
    public void Non_boolean_condition_throws()
    {
        FluentActions.Invoking(() => Engine.EvaluateCondition("data.count", Scope(Order)))
            .Should().Throw<InvalidOperationException>().WithMessage("*expected bool*");
    }

    [Fact]
    public void Whole_numbers_surface_as_long_and_others_as_double()
    {
        Engine.Evaluate("data.amount", Scope(Order)).Should().Be(7500.5d);
        Engine.Evaluate("data.count + 1", Scope(Order)).Should().Be(4L);
    }

    [Fact]
    public void User_values_and_roles_are_available()
    {
        Engine.EvaluateCondition("user.IsInRole(\"manager\")", Scope()).Should().BeTrue();
        Engine.EvaluateCondition("user.IsInRole(\"admin\")", Scope()).Should().BeFalse();
        Engine.Evaluate("user.Name", Scope()).Should().Be("alice");
        Engine.Evaluate("user.Id", Scope()).Should().Be("42");

        // The system user (timers, jobs) passes every role check.
        Engine.EvaluateCondition("user.IsInRole(\"anything\")", Scope(user: WorkflowUser.System)).Should().BeTrue();
    }

    [Fact]
    public void Now_input_child_and_instance_are_available()
    {
        Engine.Evaluate("now", Scope()).Should().Be(FixedNow);
        Engine.Evaluate("now.Year", Scope()).Should().Be(2026);
        Engine.EvaluateCondition("now.AddDays(1) > now", Scope()).Should().BeTrue();
        Engine.Evaluate("input.comment", Scope(input: """{ "comment": "ok" }""")).Should().Be("ok");
        Engine.Evaluate("input.comment", Scope()).Should().BeNull();
        Engine.EvaluateCondition("child.subType == \"Success\" && child.data.score > 50", Scope(child: """{ "subType": "Success", "data": { "score": 85 } }"""))
            .Should().BeTrue();
        Engine.Evaluate("instance.key + \"/\" + instance.state", Scope()).Should().Be("order-7/review");
    }

    [Fact]
    public void Conditional_and_arithmetic_expressions_produce_json_values()
    {
        Engine.EvaluateToNode("data.amount > 20000 ? 40 : 85", Scope(Order))!.GetValue<int>().Should().Be(85);
        Engine.EvaluateToNode("data.customer.address", Scope(Order))!.AsObject()["city"]!.GetValue<string>().Should().Be("Ankara");
        Engine.EvaluateToNode("data.customer.tags", Scope(Order))!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("a", "b");
        Engine.EvaluateToNode("data.missing", Scope(Order)).Should().BeNull();
    }

    [Fact]
    public void Template_with_a_single_placeholder_keeps_the_value_type()
    {
        var rendered = Engine.Render(JsonValue.Create("{{ data.amount }}"), Scope(Order));

        rendered!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        rendered.GetValue<double>().Should().Be(7500.5d);
        Engine.Render(JsonValue.Create("{{ data.vip }}"), Scope(Order))!.GetValue<bool>().Should().BeTrue();
        Engine.Render(JsonValue.Create("{{ data.customer }}"), Scope(Order)).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public void Template_interpolates_text_with_invariant_culture()
    {
        Engine.RenderText("Order {{ data.count }} of {{ data.name }}: {{ data.amount }}", Scope(Order))
            .Should().Be("Order 3 of Alice Smith: 7500.5");
        Engine.RenderText("no placeholders", Scope(Order)).Should().Be("no placeholders");
    }

    [Fact]
    public void Template_renders_nested_objects_and_arrays_and_keeps_literals()
    {
        var template = JsonNode.Parse("""
            { "id": "{{ data.count }}", "label": "#{{ data.count }}", "flag": true, "n": 1,
              "nested": { "city": "{{ data.customer.address.city }}" }, "list": [ "{{ data.name }}", 2, null ] }
            """);

        var rendered = Engine.Render(template, Scope(Order))!.AsObject();

        rendered["id"]!.GetValue<long>().Should().Be(3);
        rendered["label"]!.GetValue<string>().Should().Be("#3");
        rendered["flag"]!.GetValue<bool>().Should().BeTrue();
        rendered["n"]!.GetValue<int>().Should().Be(1);
        rendered["nested"]!["city"]!.GetValue<string>().Should().Be("Ankara");
        rendered["list"]!.AsArray().Should().HaveCount(3);
        rendered["list"]![0]!.GetValue<string>().Should().Be("Alice Smith");
        rendered["list"]![2].Should().BeNull();
    }

    [Theory]
    [InlineData("data.amount > 5000")]
    [InlineData("user.IsInRole(\"manager\") && now.Year > 2000")]
    [InlineData("Math.Round(1.5)")]
    public void Valid_expressions_validate(string expression)
    {
        Engine.Validate(expression).Should().BeNull();
    }

    [Theory]
    [InlineData("data.amount >")]
    [InlineData("((")]
    [InlineData("unknownVariable > 1")]
    public void Syntax_errors_are_reported_by_validate(string expression)
    {
        Engine.Validate(expression).Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("typeof(string).Assembly")]
    [InlineData("\"x\".GetType().GetMethods()")]
    [InlineData("user.GetType().Assembly")]
    [InlineData("now.GetType().Assembly")]
    [InlineData("System.IO.File.ReadAllText(\"/etc/passwd\")")]
    [InlineData("System.IO.Directory.GetFiles(\"/\")")]
    [InlineData("System.Environment.Exit(1)")]
    [InlineData("System.Diagnostics.Process.Start(\"sh\")")]
    [InlineData("new System.Net.WebClient()")]
    [InlineData("Activator.CreateInstance(typeof(object))")]
    [InlineData("AppDomain.CurrentDomain")]
    public void Sandbox_blocks_reflection_and_system_types(string expression)
    {
        Engine.Validate(expression).Should().NotBeNull();
    }

    [Fact]
    public void Sandbox_blocks_reflection_through_dynamic_data_members()
    {
        const string expression = "data.name.GetType().Assembly.FullName";

        Engine.Validate(expression).Should().Contain("Reflection");
        FluentActions.Invoking(() => Engine.Evaluate(expression, Scope(Order))).Should().Throw<Exception>();
    }

    [Fact]
    public void Compiled_expressions_are_cached_and_reused_with_new_scopes()
    {
        Engine.EvaluateCondition("data.count > 1", Scope("""{ "count": 2 }""")).Should().BeTrue();
        Engine.EvaluateCondition("data.count > 1", Scope("""{ "count": 0 }""")).Should().BeFalse();
    }

    [Theory]
    [InlineData("PT15M", 0, 15, 0)]
    [InlineData("P2D", 48, 0, 0)]
    [InlineData("00:02:00", 0, 2, 0)]
    [InlineData("1.00:00:00", 24, 0, 0)]
    public void Durations_parse_iso8601_and_timespan(string value, int hours, int minutes, int seconds)
    {
        Durations.TryParse(value, out var duration).Should().BeTrue();
        duration.Should().Be(new TimeSpan(hours, minutes, seconds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("P1X")]
    public void Invalid_durations_are_rejected(string? value)
    {
        Durations.TryParse(value, out _).Should().BeFalse();
    }
}
