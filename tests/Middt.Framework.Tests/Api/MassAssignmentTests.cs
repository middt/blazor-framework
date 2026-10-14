using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Api;

/// <summary>Insert/update/delete only take what the client may set (BaseCrudRepository.ApplyUpdate / PrepareForInsert).</summary>
public sealed class MassAssignmentTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    private TestApiHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Document> SeedAsync(string title, int version = 1, string? approvedBy = "admin", string? auditNote = "created")
    {
        var document = new Document { Title = title, OwnerId = FakeUserStore.UserId, ApprovedBy = approvedBy, AuditNote = auditNote, Version = version };
        return await Host.ScopedAsync(async db =>
        {
            db.Documents.Add(document);
            await db.SaveChangesAsync(Ct);
            return document;
        });
    }

    private Task<Document?> LoadAsync(int id) =>
        Host.ScopedAsync(db => db.Documents.AsNoTracking().Include(d => d.Owner).Include(d => d.Attachments).SingleOrDefaultAsync(d => d.Id == id, Ct));

    private async Task<HttpResponseMessage> PostAsync(string action, object payload) =>
        await Host.Client.PostAsJsonAsync($"api/v1/documents/{action}", payload, TestApiHost.Json, Ct);

    [Fact]
    public async Task Update_changes_editable_scalars_only()
    {
        var seeded = await SeedAsync("original");

        using var response = await PostAsync("update", new
        {
            id = seeded.Id,
            title = "changed",
            ownerId = FakeUserStore.OtherUserId, // [Editable(false, AllowInitialValue = true)]
            approvedBy = "mallory", // [Editable(false)]
            auditNote = "tampered", // [ReadOnly(true)]
            titleUpper = "HACKED", // computed column
            version = 1,
            owner = new { id = FakeUserStore.UserId, name = "renamed-through-navigation" },
            attachments = new[] { new { fileName = "smuggled.exe" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await LoadAsync(seeded.Id);
        stored!.Title.Should().Be("changed");
        stored.OwnerId.Should().Be(FakeUserStore.UserId);
        stored.ApprovedBy.Should().Be("admin");
        stored.AuditNote.Should().Be("created");
        stored.TitleUpper.Should().Be("CHANGED");
        stored.Owner!.Name.Should().Be(FakeUserStore.Username);
        stored.Attachments.Should().BeEmpty();
        (await Host.ScopedAsync(db => db.Attachments.AnyAsync(a => a.FileName == "smuggled.exe", Ct))).Should().BeFalse();
    }

    [Fact]
    public async Task Update_response_is_the_stored_entity_not_the_payload()
    {
        var seeded = await SeedAsync("resp");

        using var response = await PostAsync("update", new { id = seeded.Id, title = "resp2", approvedBy = "mallory", version = 1 });

        var body = await response.Content.ReadFromJsonAsync<BaseResponseDataModel<Document>>(TestApiHost.Json, Ct);
        body!.Data!.ApprovedBy.Should().Be("admin");
        body.Data.TitleUpper.Should().Be("RESP2");
    }

    [Fact]
    public async Task Update_cannot_retarget_another_row_through_the_primary_key()
    {
        var victim = await SeedAsync("victim");

        // The key selects the row; there is no way to change it, so the payload's key decides which row is touched.
        using var response = await PostAsync("update", new { id = victim.Id, title = "victim", version = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(victim.Id))!.Id.Should().Be(victim.Id);
    }

    [Fact]
    public async Task Update_of_a_missing_key_returns_404_and_inserts_nothing()
    {
        var before = await Host.ScopedAsync(db => db.Documents.CountAsync(Ct));

        using var response = await PostAsync("update", new { id = 987654, title = "ghost", ownerId = FakeUserStore.UserId, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Host.ScopedAsync(db => db.Documents.CountAsync(Ct))).Should().Be(before);
    }

    [Fact]
    public async Task Update_with_a_stale_concurrency_token_returns_409_and_keeps_the_row()
    {
        var seeded = await SeedAsync("versioned", version: 3);

        using var response = await PostAsync("update", new { id = seeded.Id, title = "stale write", version = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await LoadAsync(seeded.Id))!.Title.Should().Be("versioned");
    }

    [Fact]
    public async Task Update_with_the_current_concurrency_token_succeeds()
    {
        var seeded = await SeedAsync("versioned-ok", version: 3);

        using var response = await PostAsync("update", new { id = seeded.Id, title = "fresh write", version = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(seeded.Id))!.Title.Should().Be("fresh write");
    }

    [Fact]
    public async Task Insert_ignores_client_identity_navigation_graph_and_server_controlled_values()
    {
        var before = await Host.ScopedAsync(db => db.Owners.CountAsync(Ct));

        using var response = await PostAsync("insert", new
        {
            id = 777777,
            title = "new doc",
            ownerId = FakeUserStore.UserId,
            approvedBy = "mallory",
            auditNote = "forged",
            titleUpper = "FORGED",
            version = 1,
            owner = new { id = 4242, name = "injected owner" },
            attachments = new[] { new { id = 99, fileName = "smuggled.exe" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await response.Content.ReadFromJsonAsync<BaseResponseDataModel<Document>>(TestApiHost.Json, Ct))!.Data!;
        created.Id.Should().NotBe(777777);
        created.Id.Should().BePositive();

        var stored = await LoadAsync(created.Id);
        stored!.Title.Should().Be("new doc");
        stored.OwnerId.Should().Be(FakeUserStore.UserId, "[Editable(false, AllowInitialValue = true)] may be set on insert");
        stored.ApprovedBy.Should().BeNull();
        stored.AuditNote.Should().BeNull();
        stored.TitleUpper.Should().Be("NEW DOC");
        stored.Attachments.Should().BeEmpty();

        (await LoadAsync(777777)).Should().BeNull();
        (await Host.ScopedAsync(db => db.Owners.CountAsync(Ct))).Should().Be(before);
        (await Host.ScopedAsync(db => db.Attachments.AnyAsync(a => a.FileName == "smuggled.exe", Ct))).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_uses_only_the_key_and_removes_the_stored_row()
    {
        var seeded = await SeedAsync("to delete", version: 5);

        // Other payload fields (even a wrong concurrency token or a different owner) are irrelevant.
        using var response = await PostAsync("delete", new { id = seeded.Id, title = "", ownerId = 12345, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseResponseDataModel<Document>>(TestApiHost.Json, Ct);
        body!.Data!.Title.Should().Be("to delete");
        (await LoadAsync(seeded.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_of_a_missing_key_returns_404()
    {
        using var response = await PostAsync("delete", new { id = 876543 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestApiHost.Json, Ct))!.Title.Should().Be("Kayıt bulunamadı.");
    }

    [Fact]
    public async Task Delete_without_a_key_returns_404()
    {
        using var response = await PostAsync("delete", new { title = "no id" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>Record level (AuthorizeAsync) and row level (ApplyAccessFilter) authorization hooks.</summary>
public sealed class AccessControlTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>, IAsyncLifetime
{
    private const string Route = "api/v1/owneddocuments";

    private string aliceToken = null!;
    private string bobToken = null!;
    private int aliceDoc;
    private int bobDoc;

    private TestApiHost Host => fixture.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        aliceToken = (await Host.LoginAsync()).access_token!;
        bobToken = (await Host.LoginAsync(FakeUserStore.OtherUsername)).access_token!;

        var docs = await Host.ScopedAsync(async db =>
        {
            var a = new Document { Title = "alice-" + Guid.NewGuid().ToString("N")[..8], OwnerId = FakeUserStore.UserId, Version = 1 };
            var b = new Document { Title = "bob-" + Guid.NewGuid().ToString("N")[..8], OwnerId = FakeUserStore.OtherUserId, Version = 1 };
            db.Documents.AddRange(a, b);
            await db.SaveChangesAsync(Ct);
            return (a.Id, b.Id);
        });
        (aliceDoc, bobDoc) = docs;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string action, string token, object? payload = null)
    {
        using var request = TestApiHost.Authorized(method, $"{Route}/{action}", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: TestApiHost.Json);
        }

        return await Host.Client.SendAsync(request, Ct);
    }

    private Task<Document?> LoadAsync(int id) =>
        Host.ScopedAsync(db => db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, Ct));

    [Fact]
    public async Task Owner_can_read_own_record()
    {
        using var response = await SendAsync(HttpMethod.Get, $"getbyid?id={aliceDoc}", aliceToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_users_record_is_404_on_getbyid()
    {
        using var response = await SendAsync(HttpMethod.Get, $"getbyid?id={bobDoc}", aliceToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "existence of foreign records is not disclosed");
    }

    [Fact]
    public async Task Other_users_record_is_404_on_update_and_unchanged()
    {
        var before = await LoadAsync(bobDoc);

        using var response = await SendAsync(HttpMethod.Post, "update", aliceToken, new { id = bobDoc, title = "pwned", version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoadAsync(bobDoc))!.Title.Should().Be(before!.Title);
    }

    [Fact]
    public async Task Other_users_record_is_404_on_delete_and_still_exists()
    {
        using var response = await SendAsync(HttpMethod.Post, "delete", aliceToken, new { id = bobDoc });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoadAsync(bobDoc)).Should().NotBeNull();
    }

    [Fact]
    public async Task Owner_can_update_and_delete_own_record()
    {
        using (var update = await SendAsync(HttpMethod.Post, "update", aliceToken, new { id = aliceDoc, title = "mine", version = 1 }))
        {
            update.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var delete = await SendAsync(HttpMethod.Post, "delete", aliceToken, new { id = aliceDoc });
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(aliceDoc)).Should().BeNull();
    }

    [Fact]
    public async Task Owner_cannot_give_away_a_record_through_update()
    {
        using var response = await SendAsync(HttpMethod.Post, "update", aliceToken, new { id = aliceDoc, title = "gift", ownerId = FakeUserStore.OtherUserId, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(aliceDoc))!.OwnerId.Should().Be(FakeUserStore.UserId);
    }

    [Fact]
    public async Task GetItems_and_GetAll_only_return_own_rows()
    {
        using var items = await SendAsync(HttpMethod.Post, "getitems", aliceToken, new BaseSearchRequestModel<Document> { RequestItemSize = 0 });
        items.StatusCode.Should().Be(HttpStatusCode.OK);
        var itemsBody = await items.Content.ReadFromJsonAsync<BaseResponseDataModel<List<Document>>>(TestApiHost.Json, Ct);
        itemsBody!.Data.Should().NotBeEmpty().And.OnlyContain(d => d.OwnerId == FakeUserStore.UserId);
        itemsBody.Data.Should().Contain(d => d.Id == aliceDoc).And.NotContain(d => d.Id == bobDoc);
        itemsBody.Count.Should().Be(itemsBody.Data!.Count, "the total count is computed after the access filter");

        using var all = await SendAsync(HttpMethod.Get, "getall", bobToken);
        var allBody = await all.Content.ReadFromJsonAsync<BaseResponseDataModel<List<Document>>>(TestApiHost.Json, Ct);
        allBody!.Data.Should().Contain(d => d.Id == bobDoc).And.OnlyContain(d => d.OwnerId == FakeUserStore.OtherUserId);
    }

    [Fact]
    public async Task Insert_for_another_owner_is_403_and_nothing_is_written()
    {
        var title = "forbidden-" + Guid.NewGuid().ToString("N")[..8];

        using var response = await SendAsync(HttpMethod.Post, "insert", aliceToken, new { title, ownerId = FakeUserStore.OtherUserId, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Host.ScopedAsync(db => db.Documents.AnyAsync(d => d.Title == title, Ct))).Should().BeFalse();
    }

    [Fact]
    public async Task Insert_for_own_owner_succeeds()
    {
        using var response = await SendAsync(HttpMethod.Post, "insert", aliceToken, new { title = "own insert", ownerId = FakeUserStore.UserId, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_access_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Route}/getbyid?id={aliceDoc}");
        using var response = await Host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
