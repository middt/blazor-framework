using System.Text.Json.Nodes;
using Middt.Framework.Blazor.Workflow;
using Middt.Framework.Model.Workflow;

namespace Middt.Framework.Tests.Blazor;

/// <summary>
/// Start dialog prefill from the URL (<c>/workflow/x?start=1&amp;key=…&amp;field=…</c>): query strings converted to typed
/// JSON by the start input schema (<see cref="WorkflowUi.StartPrefill"/>).
/// </summary>
public sealed class WorkflowStartPrefillTests
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "categoryId": { "type": "integer", "title": "Kategori", "x-lookup": { "entity": "Category", "valueField": "categoryId", "textField": "categoryName" } },
            "storeCode": { "type": "string", "x-lookup": { "entity": "Store", "valueField": "code", "textField": "name" } },
            "price": { "type": "number" },
            "active": { "type": "boolean" },
            "name": { "type": "string" },
            "quantity": { "type": ["integer", "null"] },
            "key": { "type": "string" },
            "start": { "type": "string" },
            "tags": { "type": "array" }
          }
        }
        """;

    private static JsonObject ParseSchema() => JsonNode.Parse(Schema)!.AsObject();

    private static Dictionary<string, string?> Query(params (string Name, string? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Query_values_are_converted_by_the_property_type()
    {
        var prefill = WorkflowUi.StartPrefill(ParseSchema(), Query(
            ("categoryId", "5"), ("storeCode", "IST"), ("price", "4.5"), ("active", "true"), ("name", "Yol"), ("quantity", "3")));

        prefill!.ToJsonString().Should().Be("""{"categoryId":5,"storeCode":"IST","price":4.5,"active":true,"name":"Yol","quantity":3}""");
    }

    [Fact]
    public void Start_key_unknown_and_unconvertible_values_are_ignored()
    {
        var query = Query(("start", "1"), ("key", "category-5"), ("other", "x"), ("categoryId", "abc"), ("price", "5"), ("tags", "[1]"));

        WorkflowUi.StartPrefill(ParseSchema(), query)!.ToJsonString().Should().Be("""{"price":5}""");

        // matched names (removed from the URL) include values that did not convert, never start/key/unknown names
        WorkflowUi.StartPrefillParameters(ParseSchema(), query.Keys).Should().Equal("categoryId", "price", "tags");
    }

    [Fact]
    public void Nothing_applies_without_schema_properties_or_values()
    {
        WorkflowUi.StartPrefill(null, Query(("categoryId", "5"))).Should().BeNull();
        WorkflowUi.StartPrefill(ParseSchema(), null).Should().BeNull();
        WorkflowUi.StartPrefill(ParseSchema(), Query(("other", "5"))).Should().BeNull();
        WorkflowUi.StartPrefillParameters(JsonNode.Parse("""{ "type": "object" }""")!.AsObject(), ["categoryId"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("42", "integer", "42")]
    [InlineData("-7", "integer", "-7")]
    [InlineData("4.5", "integer", null)]
    [InlineData("4.5", "number", "4.5")]
    [InlineData("1e3", "number", "1000")]
    [InlineData("x", "number", null)]
    [InlineData("TRUE", "boolean", "true")]
    [InlineData("0", "boolean", "false")]
    [InlineData("yes", "boolean", null)]
    [InlineData("42", "string", "\"42\"")]
    [InlineData("2026-01-02", null, "\"2026-01-02\"")]
    [InlineData("{}", "object", null)]
    [InlineData("", "string", null)]
    public void Query_values_follow_the_schema_type(string text, string? type, string? json) =>
        (WorkflowUi.QueryValue(text, type)?.ToJsonString()).Should().Be(json);

    [Fact]
    public void Type_arrays_use_the_first_non_null_type() =>
        WorkflowUi.SchemaType(JsonNode.Parse("""{ "type": ["null", "number"] }""")!.AsObject()).Should().Be("number");

    [Fact]
    public void WorkflowGraph_computes_layered_nodes_current_state_and_traversed_paths()
    {
        var definition = new WorkflowDefinitionModel
        {
            Key = "customer-onboarding",
            Version = 1,
            StartState = "kyc-scoring",
            States =
            [
                new() { Key = "kyc-scoring", Title = "KYC Skorlama", Type = "Intermediate", SubType = "None", Step = 1 },
                new() { Key = "kyc-review", Title = "KYC İncelemesi", Type = "Intermediate", SubType = "Human", Step = 2 },
                new() { Key = "active", Title = "Aktif Müşteri", Type = "Intermediate", SubType = "Human", Step = 3 },
                new() { Key = "closed", Title = "Kapatıldı", Type = "Finish", SubType = "Cancelled", Step = 4 },
                new() { Key = "rejected", Title = "Reddedildi", Type = "Finish", SubType = "Error", Step = 4 },
            ],
            Transitions =
            [
                new() { Key = "auto-approve", From = "kyc-scoring", Target = "active", Trigger = "Automatic" },
                new() { Key = "require-review", From = "kyc-scoring", Target = "kyc-review", Trigger = "Automatic" },
                new() { Key = "approve-kyc", Title = "KYC Onayla", From = "kyc-review", Target = "active", Trigger = "Manual" },
                new() { Key = "add-kyc-note", Title = "Not Ekle", From = "kyc-review", Target = "$self", Trigger = "Manual" },
                new() { Key = "reject-kyc", Title = "Reddet", From = "kyc-review", Target = "rejected", Trigger = "Manual" },
                new() { Key = "update-income", Title = "Gelir Güncelle", From = "active", Target = "kyc-scoring", Trigger = "Manual" },
                new() { Key = "close-account", Title = "Kapat", From = "active", Target = "closed", Trigger = "Manual" },
            ],
        };

        var instance = new WorkflowInstanceModel
        {
            Id = Guid.NewGuid(),
            Workflow = "customer-onboarding",
            WorkflowVersion = 1,
            State = "kyc-review",
            Status = WorkflowInstanceStatus.Active,
            AvailableTransitions =
            [
                new() { Key = "approve-kyc", Title = "KYC Onayla", Target = "active" },
                new() { Key = "add-kyc-note", Title = "Not Ekle", Target = "$self" },
                new() { Key = "reject-kyc", Title = "Reddet", Target = "rejected" },
            ],
        };

        var history = new List<WorkflowHistoryModel>
        {
            new() { TransitionKey = "start", FromState = null, ToState = "kyc-scoring", Trigger = "Start" },
            new() { TransitionKey = "require-review", FromState = "kyc-scoring", ToState = "kyc-review", Trigger = "Automatic" },
            new() { TransitionKey = "add-kyc-note", FromState = "kyc-review", ToState = "kyc-review", Trigger = "Manual" },
        };

        var graph = WorkflowGraph.Build(definition, instance, history);

        graph.IsEmpty.Should().BeFalse();
        graph.Nodes.Should().HaveCount(5);

        var scoringNode = graph.Nodes.Single(n => n.Key == "kyc-scoring");
        var reviewNode = graph.Nodes.Single(n => n.Key == "kyc-review");
        var activeNode = graph.Nodes.Single(n => n.Key == "active");
        var closedNode = graph.Nodes.Single(n => n.Key == "closed");
        var rejectedNode = graph.Nodes.Single(n => n.Key == "rejected");

        scoringNode.Column.Should().Be(0);
        reviewNode.Column.Should().Be(1);
        activeNode.Column.Should().Be(2);
        closedNode.Column.Should().Be(3);
        rejectedNode.Column.Should().Be(3);

        scoringNode.IsVisited.Should().BeTrue();
        scoringNode.IsCurrent.Should().BeFalse();
        reviewNode.IsCurrent.Should().BeTrue();
        reviewNode.VisitCount.Should().Be(2);
        activeNode.IsVisited.Should().BeFalse();

        graph.Edges.Single(e => e.Id == "__start->kyc-scoring").IsTraversed.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "kyc-scoring->kyc-review").IsTraversed.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "kyc-scoring->kyc-review").OrderBadge.Should().Be("2");
        graph.Edges.Single(e => e.Id == "kyc-review->kyc-review").IsSelfLoop.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "kyc-review->kyc-review").IsTraversed.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "kyc-review->kyc-review").OrderBadge.Should().Be("3");
        graph.Edges.Single(e => e.Id == "kyc-review->active").IsAvailable.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "active->kyc-scoring").IsBackEdge.Should().BeTrue();
    }
}

