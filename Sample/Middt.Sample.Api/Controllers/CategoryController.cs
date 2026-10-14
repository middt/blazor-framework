using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
using Middt.Sample.Common.Messages;
using Rebus.Bus;

namespace Middt.Sample.Api.Controllers;

/// <summary>Category CRUD; publishes <see cref="CategoryCreated"/> after a successful insert.</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class CategoryController(CategoryRepository repository, IBus bus, TimeProvider timeProvider)
    : BaseCrudControllerWithAuth<Category, CategoryRepository>(repository)
{
    public override async Task<ActionResult<BaseResponseDataModel<Category>>> Insert([FromBody] Category model, CancellationToken cancellationToken)
    {
        var result = await base.Insert(model, cancellationToken);

        if (result.Value is { IsSuccess: true, Data: { } category })
        {
            // Published after the commit. For guaranteed delivery together with the database write use Rebus' transactional outbox.
            await bus.Publish(new CategoryCreated(category.CategoryId, category.CategoryName, timeProvider.GetUtcNow()));
        }

        return result;
    }
}
