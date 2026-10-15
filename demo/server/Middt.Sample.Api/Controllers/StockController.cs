using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Stocks per store and product; list rows carry <c>storeName</c> and <c>productName</c>. Changes go through the stock-replenishment workflow.</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class StockController(StockRepository repository) : SampleCrudController<Stock, StockRepository>(repository)
{
    protected override bool ChangedThroughWorkflows => true;
}
