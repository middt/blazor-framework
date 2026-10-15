using System.Linq.Expressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Categories (read only: they change through the category-request lifecycle workflow, which also publishes CategoryCreated).</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class CategoryController(CategoryRepository repository) : SampleCrudController<Category, CategoryRepository>(repository)
{
    protected override bool ChangedThroughWorkflows => true;

    protected override Expression<Func<Category, bool>> ActiveRecords => c => c.Active;
}
