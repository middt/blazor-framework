using System.Linq.Expressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Api.Cache;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>
/// Stores (read only: they change through the store-opening lifecycle workflow); the active store list used by lookups is
/// cached (HybridCache: memory + Redis when configured).
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class StoreController(StoreRepository repository, IBaseCache cache) : SampleCrudController<Store, StoreRepository>(repository)
{
    /// <summary>Cache key of the store list (removed by the bike-* tasks that change stores).</summary>
    public const string AllStoresCacheKey = $"{nameof(StoreController)}:{nameof(GetAll)}";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    protected override bool ChangedThroughWorkflows => true;

    protected override Expression<Func<Store, bool>> ActiveRecords => s => s.Active;

    [HttpGet("[action]")]
    public override async Task<ActionResult<BaseResponseDataModel<List<Store>>>> GetAll(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            AllStoresCacheKey,
            async ct => (await base.GetAll(ct)).Value!,
            CacheDuration,
            cancellationToken);
}
