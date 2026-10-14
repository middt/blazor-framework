using System.Text;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Security;
using Middt.Framework.Tests.Infrastructure;

namespace Middt.Framework.Tests.Security;

public sealed class RefreshTokenStoreTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly TestDistributedCache cache;
    private readonly DistributedCacheRefreshTokenStore store;

    public RefreshTokenStoreTests()
    {
        cache = new TestDistributedCache(time);
        store = new DistributedCacheRefreshTokenStore(cache, time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RefreshTokenEntry Entry(string family = "family-1", long userId = 7, TimeSpan? lifetime = null) =>
        new(userId, family, time.GetUtcNow() + (lifetime ?? TimeSpan.FromHours(1)));

    [Fact]
    public async Task Stored_token_is_found_and_consumed_once()
    {
        await store.StoreAsync("token-a", Entry(), Ct);

        (await store.FindAsync("token-a", Ct))!.UserId.Should().Be(7);

        var first = await store.ConsumeAsync("token-a", Ct);
        first.Status.Should().Be(RefreshTokenStatus.Valid);
        first.Entry!.FamilyId.Should().Be("family-1");
        first.Entry.ConsumedAt.Should().Be(time.GetUtcNow());
    }

    [Fact]
    public async Task Unknown_token_is_NotFound()
    {
        var result = await store.ConsumeAsync("never-issued", Ct);

        result.Status.Should().Be(RefreshTokenStatus.NotFound);
        result.Entry.Should().BeNull();
    }

    [Fact]
    public async Task Second_consumption_is_Reused_and_revokes_the_whole_family()
    {
        await store.StoreAsync("token-a", Entry(), Ct);
        (await store.ConsumeAsync("token-a", Ct)).Status.Should().Be(RefreshTokenStatus.Valid);

        // Rotation issued a sibling in the same family.
        await store.StoreAsync("token-b", Entry(), Ct);

        var replay = await store.ConsumeAsync("token-a", Ct);
        replay.Status.Should().Be(RefreshTokenStatus.Reused);
        replay.Entry!.UserId.Should().Be(7);

        (await store.ConsumeAsync("token-b", Ct)).Status.Should().Be(RefreshTokenStatus.Revoked, "reuse kills every token of the family");
    }

    [Fact]
    public async Task Revoking_a_family_does_not_affect_other_families()
    {
        await store.StoreAsync("token-a", Entry("family-1"), Ct);
        await store.StoreAsync("token-x", Entry("family-2"), Ct);

        await store.RevokeFamilyAsync("family-1", time.GetUtcNow().AddHours(1), Ct);

        (await store.ConsumeAsync("token-a", Ct)).Status.Should().Be(RefreshTokenStatus.Revoked);
        (await store.ConsumeAsync("token-x", Ct)).Status.Should().Be(RefreshTokenStatus.Valid);
    }

    [Fact]
    public async Task Token_past_the_family_expiry_is_NotFound()
    {
        await store.StoreAsync("token-a", Entry(lifetime: TimeSpan.FromMinutes(5)), Ct);

        time.Advance(TimeSpan.FromMinutes(5));

        (await store.ConsumeAsync("token-a", Ct)).Status.Should().Be(RefreshTokenStatus.NotFound);
    }

    [Fact]
    public async Task Token_just_before_expiry_is_still_valid()
    {
        await store.StoreAsync("token-a", Entry(lifetime: TimeSpan.FromMinutes(5)), Ct);

        time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        (await store.ConsumeAsync("token-a", Ct)).Status.Should().Be(RefreshTokenStatus.Valid);
    }

    [Fact]
    public async Task Cache_keys_and_values_never_contain_the_raw_token()
    {
        const string token = "super-secret-refresh-token-value";
        await store.StoreAsync(token, Entry(), Ct);
        await store.ConsumeAsync(token, Ct);

        cache.Keys.Should().ContainSingle();
        cache.Keys.Should().AllSatisfy(k => k.Should().NotContain(token));
        cache.RawValues.Should().AllSatisfy(v => Encoding.UTF8.GetString(v).Should().NotContain(token));

        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        cache.Keys.Single().Should().EndWith(sha256);
    }

    [Fact]
    public async Task Revocation_marker_outlives_nothing_beyond_the_family_expiry()
    {
        await store.StoreAsync("token-a", Entry(lifetime: TimeSpan.FromMinutes(10)), Ct);
        await store.RevokeFamilyAsync("family-1", time.GetUtcNow().AddMinutes(10), Ct);
        cache.Keys.Should().HaveCount(2);

        time.Advance(TimeSpan.FromMinutes(11));

        (await store.FindAsync("token-a", Ct)).Should().BeNull("entries expire with the family");
        (await store.ConsumeAsync("token-a", Ct)).Status.Should().Be(RefreshTokenStatus.NotFound);
    }
}
