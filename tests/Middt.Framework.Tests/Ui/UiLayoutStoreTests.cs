using Microsoft.EntityFrameworkCore;
using Middt.Framework.Expressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Server.Runtime;

namespace Middt.Framework.Tests.Ui;

public sealed class UiLayoutStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UiLayoutDocument Doc(string key, string kind = "page", int version = 1, string roles = "[]", string text = "Hello", string extra = "") =>
        UiJson.Parse(UiTestHost.Layout(key, kind, version, roles, $$"""[ { "widget": "heading", "text": "{{text}}" } ]""", extra));

    private static ExpressionUser User(params string[] roles) => new() { Name = "alice", Roles = roles };

    [Fact]
    public async Task Saving_a_new_layout_stores_version_1()
    {
        await using var host = await UiTestHost.StartAsync();

        var saved = await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", version: 7), null, "alice", Ct));
        var found = await host.WithStoreAsync(s => s.FindAsync("orders", Ct));
        var row = await host.WithDbAsync(db => db.Layouts.AsNoTracking().SingleAsync(Ct));

        saved.Version.Should().Be(1, "new layouts start at version 1 whatever the document says");
        found!.Version.Should().Be(1);
        found.Body[0].StringProp("text").Should().Be("Hello");
        row.Source.Should().Be(UiLayoutStore.SourceApi);
        row.UpdatedBy.Should().Be("alice");
        row.UpdatedAt.Should().Be(host.Time.GetUtcNow());
        (await host.WithStoreAsync(s => s.RevisionsAsync("orders", Ct))).Should().BeEmpty();
    }

    [Fact]
    public async Task Updates_require_the_current_version_and_keep_revisions()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "v1"), null, "alice", Ct));

        var v2 = await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "v2"), 1, "bob", Ct));
        var stale = () => host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "stale"), 1, "eve", Ct));
        var blind = () => host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "blind"), null, "eve", Ct));

        v2.Version.Should().Be(2);
        await stale.Should().ThrowAsync<UiLayoutConflictException>().WithMessage("*version 2*");
        await blind.Should().ThrowAsync<UiLayoutConflictException>();

        var revisions = await host.WithStoreAsync(s => s.RevisionsAsync("orders", Ct));
        revisions.Should().ContainSingle();
        revisions[0].Version.Should().Be(1);
        revisions[0].CreatedBy.Should().Be("bob");
        UiJson.Parse(revisions[0].Content).Body[0].StringProp("text").Should().Be("v1");
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("v2");
    }

    [Fact]
    public async Task Revisions_are_listed_newest_first()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders"), null, "a", Ct));
        for (var version = 1; version <= 3; version++)
        {
            var expected = version;
            await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: $"v{expected + 1}"), expected, "a", Ct));
        }

        (await host.WithStoreAsync(s => s.RevisionsAsync("orders", Ct))).Select(r => r.Version).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task Seeding_inserts_and_only_overwrites_with_a_higher_version()
    {
        await using var host = await UiTestHost.StartAsync();

        (await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 2, text: "seed v2"), Ct))).Should().BeTrue();
        (await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 2, text: "same"), Ct))).Should().BeFalse();
        (await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 1, text: "lower"), Ct))).Should().BeFalse();
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("seed v2");

        (await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 3, text: "seed v3"), Ct))).Should().BeTrue();
        var found = await host.WithStoreAsync(s => s.FindAsync("orders", Ct));
        found!.Version.Should().Be(3);
        found.Body[0].StringProp("text").Should().Be("seed v3");
        (await host.WithDbAsync(db => db.Layouts.SingleAsync(Ct))).Source.Should().Be(UiLayoutStore.SourceSeed);
        (await host.WithStoreAsync(s => s.RevisionsAsync("orders", Ct))).Select(r => r.Version).Should().Equal(2);
    }

    [Fact]
    public async Task Runtime_edits_survive_seeds_with_the_same_version()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 1, text: "seed"), Ct));
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "edited"), 1, "alice", Ct));

        (await host.WithStoreAsync(s => s.SeedAsync(Doc("orders", version: 2, text: "seed again"), Ct))).Should().BeFalse();
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("edited");
    }

    [Fact]
    public async Task Delete_removes_the_layout_and_keeps_a_revision()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders"), null, "alice", Ct));
        await host.WithStoreAsync(s => s.FindAsync("orders", Ct));

        var stale = () => host.WithStoreAsync(s => s.DeleteAsync("orders", 5, "bob", Ct));
        await stale.Should().ThrowAsync<UiLayoutConflictException>();

        (await host.WithStoreAsync(s => s.DeleteAsync("orders", 1, "bob", Ct))).Should().BeTrue();
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct))).Should().BeNull("the cache is invalidated by the delete");
        (await host.WithStoreAsync(s => s.DeleteAsync("orders", null, "bob", Ct))).Should().BeFalse();
        (await host.WithStoreAsync(s => s.RevisionsAsync("orders", Ct))).Should().ContainSingle(r => r.Version == 1 && r.CreatedBy == "bob");
    }

    [Fact]
    public async Task Invalid_layouts_are_rejected_and_not_stored()
    {
        await using var host = await UiTestHost.StartAsync();
        var invalid = UiJson.Parse("""{ "key": "orders", "body": [ { "widget": "heading" }, { "widget": "marquee" } ] }""");

        var save = () => host.WithStoreAsync(s => s.SaveAsync(invalid, null, "alice", Ct));
        var seed = () => host.WithStoreAsync(s => s.SeedAsync(invalid, Ct));

        (await save.Should().ThrowAsync<UiLayoutValidationException>()).Which.Errors
            .Should().Equal("body[0](heading).text: required.", "body[1].widget: unknown widget 'marquee'.");
        await seed.Should().ThrowAsync<UiLayoutValidationException>();
        (await host.WithDbAsync(db => db.Layouts.CountAsync(Ct))).Should().Be(0);
    }

    [Fact]
    public async Task Layouts_larger_than_the_limit_are_rejected()
    {
        await using var host = await UiTestHost.StartAsync(configure: o => o.MaxLayoutLength = 300);

        var result = await host.WithStoreAsync(s => Task.FromResult(s.Validate(Doc("orders", text: new string('x', 400)))));

        result.Errors.Should().Equal("Layout is larger than 300 characters.");
        (await host.WithStoreAsync(s => Task.FromResult(s.Validate(Doc("orders"))))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CanOpen_checks_layout_roles_and_the_admin_role()
    {
        await using var host = await UiTestHost.StartAsync();
        var open = Doc("open");
        var restricted = Doc("restricted", roles: """["manager", "auditor"]""");

        var results = await host.WithStoreAsync(s => Task.FromResult(new[]
        {
            s.CanOpen(open, User()),
            s.CanOpen(restricted, User("auditor")),
            s.CanOpen(restricted, User("clerk")),
            s.CanOpen(restricted, User(UiTestHost.Admin)),
            s.CanOpen(restricted, new ExpressionUser { IsSystem = true }),
        }));

        results.Should().Equal(true, true, false, true, true);
    }

    [Fact]
    public async Task List_filters_by_kind_and_roles_and_orders_by_menu()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(async s =>
        {
            await s.SaveAsync(Doc("zeta"), null, "a", Ct);
            await s.SaveAsync(Doc("beta", extra: """ "menu": { "title": "B", "order": 2 }, """), null, "a", Ct);
            await s.SaveAsync(Doc("alpha", extra: """ "menu": { "title": "A", "order": 1 }, """), null, "a", Ct);
            await s.SaveAsync(Doc("secret", roles: """["manager"]"""), null, "a", Ct);
            await s.SaveAsync(Doc("card", kind: "view"), null, "a", Ct);
            return true;
        });

        var user = await host.WithStoreAsync(s => s.ListAsync(User(), null, Ct));
        var manager = await host.WithStoreAsync(s => s.ListAsync(User("manager"), null, Ct));
        var views = await host.WithStoreAsync(s => s.ListAsync(User(), UiLayoutKind.View, Ct));
        var pages = await host.WithStoreAsync(s => s.ListAsync(User(), UiLayoutKind.Page, Ct));

        user.Select(l => l.Key).Should().Equal("alpha", "beta", "card", "zeta");
        manager.Select(l => l.Key).Should().Equal("alpha", "beta", "card", "secret", "zeta");
        views.Select(l => l.Key).Should().Equal("card");
        pages.Select(l => l.Key).Should().NotContain("card");
        user[0].Menu!.Title.Should().Be("A");
        user[0].Title.Should().Be("alpha title");
        user[0].Version.Should().Be(1);
    }

    [Fact]
    public async Task Reads_are_cached_until_a_write_or_invalidate()
    {
        await using var host = await UiTestHost.StartAsync();
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "one"), null, "a", Ct));
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("one");

        // An external change (another node, SQL) is not seen while the snapshot is cached...
        var changed = UiJson.Serialize(Doc("orders", text: "external"));
        await host.WithDbAsync(db => db.Layouts.ExecuteUpdateAsync(u => u.SetProperty(l => l.Content, changed), Ct));
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("one");

        // ...until the snapshot is invalidated.
        await host.WithStoreAsync(s => { s.Invalidate(); return Task.FromResult(0); });
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("external");

        // Writes through any store instance invalidate the shared snapshot.
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "two"), 1, "a", Ct));
        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("two");
        (await host.WithStoreAsync(s => s.ListAsync(User(), null, Ct))).Single().Version.Should().Be(2);
    }

    [Fact]
    public async Task Cached_snapshot_expires_after_the_cache_duration()
    {
        await using var host = await UiTestHost.StartAsync(configure: o => o.CacheDuration = TimeSpan.FromMilliseconds(1));
        await host.WithStoreAsync(s => s.SaveAsync(Doc("orders", text: "one"), null, "a", Ct));
        await host.WithStoreAsync(s => s.FindAsync("orders", Ct));

        var changed = UiJson.Serialize(Doc("orders", text: "external"));
        await host.WithDbAsync(db => db.Layouts.ExecuteUpdateAsync(u => u.SetProperty(l => l.Content, changed), Ct));
        await Task.Delay(50, Ct);

        (await host.WithStoreAsync(s => s.FindAsync("orders", Ct)))!.Body[0].StringProp("text").Should().Be("external");
    }
}
