using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Framework.Tests.Workflow;

/// <summary><c>input.format: jsonPatch</c>: a client changes data with an RFC 6902 JSON Patch through a transition.</summary>
public sealed class WorkflowPatchInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Customer = """
        {
          "key": "customer",
          "start": { "key": "start", "target": "active",
            "input": { "schema": { "type": "object", "properties": { "name": { "type": "string" }, "contact": { "type": "object" } } } } },
          "states": [
            { "key": "active", "transitions": [
                { "key": "update-contact", "target": "$self",
                  "input": { "format": "jsonPatch", "into": "contact",
                    "schema": { "type": "object", "additionalProperties": false,
                      "properties": { "phone": { "type": "string", "maxLength": 10 }, "fax": { "type": "string" }, "version": { "type": "integer" },
                                      "emails": { "type": "array", "items": { "type": "string" } } } },
                    "set": { "contactUpdatedBy": "user.Name" } } },
                { "key": "close", "target": "closed" } ] },
            { "key": "closed", "type": "Finish" }
          ]
        }
        """;

    private static async Task<(WorkflowTestHost Host, Guid Id)> StartAsync()
    {
        var host = await WorkflowTestHost.StartAsync(Customer);
        var instance = await host.StartInstanceAsync("customer", new { name = "Ada", contact = new { phone = "111", fax = "222", version = 1, emails = new[] { "a@x" } } });
        return (host, instance.Id);
    }

    private static JsonObject Patch(string json) => new() { ["patch"] = JsonNode.Parse(json) };

    [Fact]
    public async Task The_patch_is_applied_to_the_data_at_into_and_set_expressions_still_run()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var result = await host.TransitionAsync(id, "update-contact", Patch("""
            [ { "op": "test", "path": "/version", "value": 1 },
              { "op": "replace", "path": "/phone", "value": "555" },
              { "op": "remove", "path": "/fax" },
              { "op": "add", "path": "/emails/-", "value": "b@x" },
              { "op": "replace", "path": "/version", "value": 2 } ]
            """));

        result.Data["contact"]!.ToJsonString().Should().Be("""{"phone":"555","version":2,"emails":["a@x","b@x"]}""");
        result.Data["name"]!.GetValue<string>().Should().Be("Ada", "the patch cannot reach outside 'into'");
        result.Data["contactUpdatedBy"]!.GetValue<string>().Should().Be("alice");

        var row = await host.WithDbAsync(db => db.History.AsNoTracking().Where(h => h.InstanceId == id).OrderBy(h => h.Id).LastAsync(Ct));
        row.TransitionKey.Should().Be("update-contact");
        JsonNode.Parse(row.Input!)!["patch"]!.AsArray().Should().HaveCount(5, "the client's patch is the transition input in the history");
    }

    [Fact]
    public async Task A_bare_array_body_over_http_is_the_patch()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, $"api/workflows/customer/instances/{id}/transitions/update-contact",
            JsonNode.Parse("""[ { "op": "replace", "path": "/phone", "value": "999" } ]""")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.GetAsync(id)).Data["contact"]!["phone"]!.GetValue<string>().Should().Be("999");
    }

    [Fact]
    public async Task A_failed_test_operation_is_a_conflict_and_changes_nothing()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var act = () => host.TransitionAsync(id, "update-contact", Patch("""
            [ { "op": "replace", "path": "/phone", "value": "555" }, { "op": "test", "path": "/version", "value": 7 } ]
            """));

        await act.Should().ThrowAsync<WorkflowConflictException>().WithMessage("*test*");
        var instance = await host.GetAsync(id);
        instance.Status.Should().Be(WorkflowInstanceStatus.Active, "a rejected patch is not a fault");
        instance.Data["contact"]!["phone"]!.GetValue<string>().Should().Be("111");
    }

    [Theory]
    [InlineData("""[ { "op": "remove", "path": "/missing" } ]""", "does not exist")]
    [InlineData("""[ { "op": "merge", "path": "/phone", "value": "1" } ]""", "'op' must be")]
    [InlineData("""[ { "op": "replace", "path": "/phone", "value": "12345678901" } ]""", "phone")]
    [InlineData("""[ { "op": "add", "path": "/unknown", "value": 1 } ]""", "unknown")]
    [InlineData("""{ "op": "replace" }""", "must be a JSON Patch")]
    public async Task An_invalid_patch_or_a_result_the_schema_rejects_is_a_validation_error(string patch, string error)
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var act = () => host.TransitionAsync(id, "update-contact", Patch(patch));

        (await act.Should().ThrowAsync<WorkflowValidationException>()).Which.Errors.Should().Contain(e => e.Contains(error, StringComparison.OrdinalIgnoreCase));
        (await host.GetAsync(id)).Status.Should().Be(WorkflowInstanceStatus.Active);
    }

    [Fact]
    public async Task Patch_transitions_cannot_run_asynchronously_and_offer_their_format_to_uis()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var act = () => host.TransitionAsync(id, "update-contact", Patch("""[ { "op": "replace", "path": "/phone", "value": "5" } ]"""), runAsync: true);
        await act.Should().ThrowAsync<WorkflowValidationException>();

        var available = (await host.GetAsync(id, WorkflowTestHost.User())).AvailableTransitions;
        available.Single(t => t.Key == "update-contact").Should().Match<AvailableTransitionModel>(t => t.InputFormat == "jsonPatch" && t.InputInto == "contact");
        available.Single(t => t.Key == "close").InputFormat.Should().BeNull();
    }

    [Theory]
    [InlineData("\"input\": { \"format\": \"jsonPatch\", \"schema\": { \"type\": \"object\" } }", "needs 'into'")]
    [InlineData("\"input\": { \"format\": \"jsonPatch\", \"into\": \"contact\" }", "needs 'schema'")]
    [InlineData("\"trigger\": \"Event\", \"event\": \"x\", \"input\": { \"format\": \"jsonPatch\", \"into\": \"c\", \"schema\": { \"type\": \"object\" } }", "event transitions cannot take a jsonPatch")]
    public async Task Invalid_patch_definitions_fail_at_load(string transition, string error)
    {
        var definition = $$"""
            { "key": "bad", "start": { "key": "s", "target": "a" },
              "states": [ { "key": "a", "transitions": [ { "key": "x", "target": "$self", {{transition}} } ] } ] }
            """;
        var act = () => WorkflowTestHost.StartAsync(definition);
        (await act.Should().ThrowAsync<WorkflowDefinitionException>()).Which.Message.Should().Contain(error);
    }
}
