using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Web.Ui;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Blazor;

public sealed class UiLookupCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Data client whose <c>getall</c> responses are completed by the test.</summary>
    private sealed class ControlledDataClient() : UiDataClient(
        new HttpClient(), new FakeSessionState("token"), new FakeTokenService(_ => new TokenResponseModel()),
        Options.Create(new ApiSettings { URL = "http://localhost", Version = "1" }), NullLogger<UiDataClient>.Instance)
    {
        public List<(string Entity, CancellationToken Token, TaskCompletionSource<BaseResponseDataModel<List<JsonObject>>> Response)> Calls { get; } = [];

        public override Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(string entity, CancellationToken cancellationToken = default)
        {
            var response = new TaskCompletionSource<BaseResponseDataModel<List<JsonObject>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
            Calls.Add((entity, cancellationToken, response));
            return response.Task;
        }
    }

    private static BaseResponseDataModel<List<JsonObject>> Items(params string[] names) =>
        BaseResponseDataModel<List<JsonObject>>.Success([.. names.Select(n => new JsonObject { ["name"] = n })]);

    [Fact]
    public async Task A_cancelled_caller_does_not_cancel_or_poison_the_shared_load()
    {
        var client = new ControlledDataClient();
        using var cache = new UiLookupCache(client, new FakeTimeProvider());
        using var first = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var firstCall = cache.GetAllAsync("Category", first.Token);
        var secondCall = cache.GetAllAsync("Category", Ct);
        await first.CancelAsync();

        await FluentActions.Awaiting(() => firstCall).Should().ThrowAsync<OperationCanceledException>();
        client.Calls.Should().ContainSingle();
        client.Calls[0].Token.IsCancellationRequested.Should().BeFalse("the request runs with the cache's token, not the caller's");

        client.Calls[0].Response.SetResult(Items("a", "b"));
        (await secondCall).Should().HaveCount(2);

        // Later callers (e.g. the next page's selects) get the cached list instead of a TaskCanceledException.
        (await cache.GetAllAsync("Category", Ct)).Should().HaveCount(2);
        client.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_loads_are_evicted_and_retried()
    {
        var client = new ControlledDataClient();
        using var cache = new UiLookupCache(client, new FakeTimeProvider());

        var failing = cache.GetAllAsync("Category", Ct);
        client.Calls[0].Response.SetResult(BaseResponseDataModel<List<JsonObject>>.Failure("boom"));
        await FluentActions.Awaiting(() => failing).Should().ThrowAsync<InvalidOperationException>();

        var retry = cache.GetAllAsync("Category", Ct);
        client.Calls.Should().HaveCount(2);
        client.Calls[1].Response.SetResult(Items("a"));
        (await retry).Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelled_loads_are_evicted_and_retried()
    {
        var client = new ControlledDataClient();
        using var cache = new UiLookupCache(client, new FakeTimeProvider());

        var cancelled = cache.GetAllAsync("Category", Ct);
        client.Calls[0].Response.SetCanceled(Ct);
        await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();

        var retry = cache.GetAllAsync("Category", Ct);
        client.Calls.Should().HaveCount(2);
        client.Calls[1].Response.SetResult(Items("a"));
        (await retry).Should().ContainSingle();
    }

    [Fact]
    public async Task Lists_are_shared_for_a_minute_then_reloaded()
    {
        var client = new ControlledDataClient();
        var time = new FakeTimeProvider();
        using var cache = new UiLookupCache(client, time);

        var load = cache.GetAllAsync("Category", Ct);
        client.Calls[0].Response.SetResult(Items("a"));
        await load;

        time.Advance(TimeSpan.FromSeconds(59));
        (await cache.GetAllAsync("Category", Ct)).Should().ContainSingle();
        client.Calls.Should().ContainSingle();

        time.Advance(TimeSpan.FromSeconds(1));
        var reload = cache.GetAllAsync("Category", Ct);
        client.Calls.Should().HaveCount(2);
        client.Calls[1].Response.SetResult(Items("a", "b"));
        (await reload).Should().HaveCount(2);
    }

    [Fact]
    public async Task Disposing_the_cache_cancels_pending_loads()
    {
        var client = new ControlledDataClient();
        var cache = new UiLookupCache(client, new FakeTimeProvider());

        var pending = cache.GetAllAsync("Category", Ct);
        cache.Dispose();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
    }
}
