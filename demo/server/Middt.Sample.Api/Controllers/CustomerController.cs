using System.Linq.Expressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

/// <summary>Customers (read only: they change through the customer-onboarding lifecycle workflow).</summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class CustomerController(CustomerRepository repository) : SampleCrudController<Customer, CustomerRepository>(repository)
{
    protected override bool ChangedThroughWorkflows => true;

    protected override Expression<Func<Customer, bool>> ActiveRecords => c => c.Active;
}
