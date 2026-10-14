using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Api.Cache;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Staff CRUD (authenticated); the staff list is cached (HybridCache: memory + Redis when configured).</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class StaffController(StaffRepository repository, IBaseCache cache) : SampleCrudController<Staff, StaffRepository>(repository)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    [HttpGet("[action]")]
    public override async Task<ActionResult<BaseResponseDataModel<List<Staff>>>> GetAll(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            $"{nameof(StaffController)}:{nameof(GetAll)}",
            async ct => (await base.GetAll(ct)).Value!,
            CacheDuration,
            cancellationToken);
}
