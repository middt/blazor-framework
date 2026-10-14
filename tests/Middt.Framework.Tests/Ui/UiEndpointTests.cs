using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Ui.Model;

namespace Middt.Framework.Tests.Ui;

public sealed class UiEndpointTests
{
    private const string Base = "api/ui";
    private const string Admin = UiTestHost.Admin;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Seeds =
    [
        UiTestHost.Layout("home", extra: """ "menu": { "title": "Home", "order": 1 }, """),
        UiTestHost.Layout("managers", roles: """["manager"]"""),
        UiTestHost.Layout("order-card", kind: "view", body: """[ { "widget": "text", "text": "Order {{ data.id }}" } ]"""),
    ];

    private static Task<UiTestHost> Host(Action<Middt.Framework.Ui.Server.MiddtUiOptions>? configure = null) => UiTestHost.StartAsync(Seeds, configure);

    private static async Task<BaseResponseDataModel<T>> EnvelopeAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<T>>(UiTestHost.Json, Ct))!;

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(UiTestHost.Json, Ct))!;
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await using var host = await Host();

        foreach (var uri in new[] { $"{Base}/layouts", $"{Base}/layouts/home", $"{Base}/layouts/home/html", $"{Base}/widgets" })
        {
            using var response = await host.SendAsync(HttpMethod.Get, uri, user: null);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, uri);
        }
    }

    [Fact]
    public async Task Layout_list_is_filtered_by_roles_and_kind()
    {
        await using var host = await Host();

        using var user = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts");
        using var manager = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts", roles: "manager");
        using var admin = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts", roles: Admin);
        using var views = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts?kind=view");

        var visible = await EnvelopeAsync<List<UiLayoutSummary>>(user);
        visible.Result.Should().Be(ResultEnum.Success);
        visible.Data!.Select(l => l.Key).Should().Equal("home", "order-card");
        visible.Count.Should().Be(2);
        visible.Data![0].Menu!.Title.Should().Be("Home");
        (await EnvelopeAsync<List<UiLayoutSummary>>(manager)).Data!.Select(l => l.Key).Should().Equal("home", "managers", "order-card");
        (await EnvelopeAsync<List<UiLayoutSummary>>(admin)).Data!.Select(l => l.Key).Should().Contain("managers");
        (await EnvelopeAsync<List<UiLayoutSummary>>(views)).Data!.Select(l => l.Key).Should().Equal("order-card");
    }

    [Fact]
    public async Task Get_returns_the_document_404_or_403()
    {
        await using var host = await Host();

        using var ok = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/home");
        using var missing = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/nope");
        using var forbidden = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/managers");
        using var allowed = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/managers", roles: "manager");

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var layout = (await EnvelopeAsync<UiLayoutDocument>(ok)).Data!;
        layout.Key.Should().Be("home");
        layout.Body[0].Widget.Should().Be("heading");
        layout.Body[0].StringProp("text").Should().Be("Hello {{ query.name }}", "widget props are serialized inline");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemAsync(missing)).Detail.Should().Be("Layout 'nope' does not exist.");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Html_endpoint_renders_encoded_html_with_query_variables()
    {
        await using var host = await Host();

        using var response = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/home/html?name=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        html.Should().Be("<div class=\"middt-ui\" data-layout=\"home\"><h2 class=\"middt-ui-heading\">Hello &lt;script&gt;alert(1)&lt;/script&gt;</h2></div>");
    }

    [Fact]
    public async Task Html_endpoint_checks_existence_and_roles()
    {
        await using var host = await Host();

        using var missing = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/nope/html");
        using var forbidden = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/managers/html");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Widgets_endpoint_lists_specs_sorted_by_name()
    {
        await using var host = await Host(o => o.AddWidget(new Middt.Framework.Ui.Widgets.UiWidgetSpec("chart", "Chart")));

        using var response = await host.SendAsync(HttpMethod.Get, $"{Base}/widgets");
        var widgets = (await EnvelopeAsync<List<UiWidgetSpecModel>>(response)).Data!;

        widgets.Select(w => w.Name).Should().BeInAscendingOrder(StringComparer.Ordinal).And.Contain(["heading", "grid", "record", "chart"]);
        widgets.Single(w => w.Name == "grid").Interactive.Should().BeTrue();
        widgets.Single(w => w.Name == "section").Children.Should().Be("required");
        widgets.Single(w => w.Name == "record").Props.Select(p => p.Name).Should().Contain("recordId");
    }

    [Fact]
    public async Task Schema_endpoint_returns_the_json_schema_including_custom_widgets()
    {
        await using var host = await Host(o => o.AddWidget(new Middt.Framework.Ui.Widgets.UiWidgetSpec("chart", "Chart")));

        using var response = await host.SendAsync(HttpMethod.Get, $"{Base}/schema");
        using var anonymous = await host.SendAsync(HttpMethod.Get, $"{Base}/schema", user: null);
        var schema = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/schema+json");
        schema["$id"]!.GetValue<string>().Should().Be(Middt.Framework.Ui.Schema.UiLayoutSchema.Id);
        schema["$defs"]!.AsObject().Select(p => p.Key).Should().Contain(["widget-heading", "widget-grid", "widget-chart"]);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "layouts/validate")]
    [InlineData("PUT", "layouts/home")]
    [InlineData("DELETE", "layouts/home")]
    [InlineData("GET", "layouts/home/revisions")]
    public async Task Admin_endpoints_require_the_admin_role(string method, string path)
    {
        await using var host = await Host();
        var body = method is "POST" or "PUT" ? UiTestHost.Layout("home") : null;

        using var user = await host.SendAsync(new HttpMethod(method), $"{Base}/{path}", body, roles: "manager");
        using var anonymous = await host.SendAsync(new HttpMethod(method), $"{Base}/{path}", body, user: null);

        user.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.WithStoreAsync(s => s.FindAsync("home", Ct)))!.Version.Should().Be(1, "nothing changed");
    }

    [Fact]
    public async Task Admin_role_is_configurable()
    {
        await using var host = await Host(o => o.AdminRole = "designer");

        using var designer = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/home/revisions", roles: "designer");
        using var defaultAdmin = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/home/revisions", roles: Admin);
        using var restricted = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/managers", roles: "designer");

        designer.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        restricted.StatusCode.Should().Be(HttpStatusCode.OK, "the admin role opens every layout");
    }

    [Fact]
    public async Task Validate_returns_the_errors_without_saving()
    {
        await using var host = await Host();

        using var invalid = await host.SendAsync(HttpMethod.Post, $"{Base}/layouts/validate", """{ "key": "Bad", "body": [ { "widget": "heading" } ] }""", roles: Admin);
        using var valid = await host.SendAsync(HttpMethod.Post, $"{Base}/layouts/validate", UiTestHost.Layout("fresh"), roles: Admin);

        invalid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeAsync<UiValidationResult>(invalid)).Data!.Errors.Should().Contain("body[0](heading).text: required.").And.HaveCount(2);
        (await EnvelopeAsync<UiValidationResult>(valid)).Data!.IsValid.Should().BeTrue();
        (await host.WithStoreAsync(s => s.FindAsync("fresh", Ct))).Should().BeNull();
    }

    [Fact]
    public async Task Put_creates_updates_and_detects_conflicts()
    {
        await using var host = await Host();

        using var created = await host.SendAsync(HttpMethod.Put, $"{Base}/layouts/fresh", UiTestHost.Layout("fresh", version: 0), roles: Admin);
        using var updated = await host.SendAsync(HttpMethod.Put, $"{Base}/layouts/fresh", UiTestHost.Layout("fresh", version: 1, body: """[ { "widget": "divider" } ]"""), roles: Admin);
        using var stale = await host.SendAsync(HttpMethod.Put, $"{Base}/layouts/fresh", UiTestHost.Layout("fresh", version: 1), roles: Admin);

        created.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeAsync<UiLayoutDocument>(created)).Data!.Version.Should().Be(1);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeAsync<UiLayoutDocument>(updated)).Data!.Version.Should().Be(2);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemAsync(stale)).Detail.Should().Contain("version 2");

        using var get = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/fresh");
        (await EnvelopeAsync<UiLayoutDocument>(get)).Data!.Body.Single().Widget.Should().Be("divider");

        using var revisions = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/fresh/revisions", roles: Admin);
        var items = (await EnvelopeAsync<List<UiLayoutRevisionModel>>(revisions)).Data!;
        items.Should().ContainSingle(r => r.Version == 1 && r.CreatedBy == "alice");
        UiJson.Parse(items[0].Content).Body[0].Widget.Should().Be("heading");
    }

    [Fact]
    public async Task Put_with_a_different_route_key_is_rejected()
    {
        await using var host = await Host();

        using var response = await host.SendAsync(HttpMethod.Put, $"{Base}/layouts/home", UiTestHost.Layout("other"), roles: Admin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).Title.Should().Be("The route key and the layout key differ.");
        (await host.WithStoreAsync(s => s.FindAsync("other", Ct))).Should().BeNull();
    }

    [Fact]
    public async Task Put_with_an_invalid_layout_returns_a_validation_problem()
    {
        await using var host = await Host();

        using var response = await host.SendAsync(HttpMethod.Put, $"{Base}/layouts/home",
            """{ "key": "home", "version": 1, "body": [ { "widget": "heading", "text": "{{ data.x + }}" }, { "widget": "nope" } ] }""", roles: Admin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(UiTestHost.Json, Ct))!;
        problem.Title.Should().Be("The layout is invalid.");
        problem.Errors["layout"].Should().HaveCount(2).And.Contain("body[1].widget: unknown widget 'nope'.");
        (await host.WithStoreAsync(s => s.FindAsync("home", Ct)))!.Body[0].StringProp("text").Should().Be("Hello {{ query.name }}");
    }

    [Fact]
    public async Task Delete_removes_layouts_and_checks_the_version()
    {
        await using var host = await Host();

        using var stale = await host.SendAsync(HttpMethod.Delete, $"{Base}/layouts/home?version=9", roles: Admin);
        using var deleted = await host.SendAsync(HttpMethod.Delete, $"{Base}/layouts/home?version=1", roles: Admin);
        using var again = await host.SendAsync(HttpMethod.Delete, $"{Base}/layouts/home", roles: Admin);
        using var get = await host.SendAsync(HttpMethod.Get, $"{Base}/layouts/home");

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Startup_seeds_layouts_and_keeps_runtime_edits_on_restart()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);

        await using (var first = await UiTestHost.StartAsync([UiTestHost.Layout("home")], connection: connection))
        {
            using var edit = await first.SendAsync(HttpMethod.Put, $"{Base}/layouts/home", UiTestHost.Layout("home", version: 1, body: """[ { "widget": "divider" } ]"""), roles: Admin);
            edit.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using (var restarted = await UiTestHost.StartAsync([UiTestHost.Layout("home", version: 2)], connection: connection))
        {
            var layout = await restarted.WithStoreAsync(s => s.FindAsync("home", Ct));
            layout!.Body.Single().Widget.Should().Be("divider", "the stored version 2 is not older than the seed");
        }

        await using var upgraded = await UiTestHost.StartAsync([UiTestHost.Layout("home", version: 3)], connection: connection);
        var current = await upgraded.WithStoreAsync(s => s.FindAsync("home", Ct));
        current!.Version.Should().Be(3);
        current.Body.Single().Widget.Should().Be("heading");
    }

    [Fact]
    public async Task Startup_fails_for_invalid_seed_files()
    {
        var invalid = () => UiTestHost.StartAsync([UiTestHost.Layout("home"), """{ "key": "broken", "body": [ { "widget": "heading" } ] }"""]);
        var malformed = () => UiTestHost.StartAsync(["{ not json"]);
        var duplicate = () => UiTestHost.StartAsync([UiTestHost.Layout("home"), UiTestHost.Layout("home")]);
        var missingDirectory = () => UiTestHost.StartAsync(configure: o => o.AddLayoutsFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        (await invalid.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*layout-01.json' is invalid*body[0](heading).text: required.*");
        await malformed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is not valid JSON*");
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("Layout key 'home' is defined more than once*");
        await missingDirectory.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Sample_layouts_seed_successfully()
    {
        var seeds = Directory.GetFiles(UiSampleLayoutTests.SampleDirectory("Layouts"), "*.json").Select(File.ReadAllText).ToList();

        await using var host = await UiTestHost.StartAsync(seeds);

        var keys = (await host.WithStoreAsync(s => s.ListAsync(new Middt.Framework.Expressions.ExpressionUser { IsSystem = true }, null, Ct))).Select(l => l.Key);
        keys.Should().BeEquivalentTo(seeds.Select(s => UiJson.Parse(s).Key));
    }
}
