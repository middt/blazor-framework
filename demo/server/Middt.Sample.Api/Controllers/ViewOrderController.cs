using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Orders (read only view: orders change through the order-approval and order-fulfillment workflows).</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ViewOrderController(ViewOrderRepository repository) : SampleCrudController<ViewOrder, ViewOrderRepository>(repository)
{
    protected override bool ChangedThroughWorkflows => true;
}
