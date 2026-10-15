using System.Linq.Expressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Products (read only: they change through the product-launch lifecycle workflow).</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ProductController(ProductRepository repository) : SampleCrudController<Product, ProductRepository>(repository)
{
    protected override bool ChangedThroughWorkflows => true;

    protected override Expression<Func<Product, bool>> ActiveRecords => p => p.Active;
}
