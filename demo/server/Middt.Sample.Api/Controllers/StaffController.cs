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
/// Staff (read only: changes go through the staff-onboarding lifecycle workflow); the active staff list used by lookups is
/// cached (HybridCache: memory + Redis when configured).
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class StaffController(StaffRepository repository, IBaseCache cache) : SampleCrudController<Staff, StaffRepository>(repository)
{
    /// <summary>Cache key of the staff list (removed by the bike-* tasks that change staff).</summary>
    public const string AllStaffCacheKey = $"{nameof(StaffController)}:{nameof(GetAll)}";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    protected override bool ChangedThroughWorkflows => true;

    protected override Expression<Func<Staff, bool>> ActiveRecords => s => s.Active == 1;

    [HttpGet("[action]")]
    public override async Task<ActionResult<BaseResponseDataModel<List<Staff>>>> GetAll(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            AllStaffCacheKey,
            async ct => (await base.GetAll(ct)).Value!,
            CacheDuration,
            cancellationToken);
}
