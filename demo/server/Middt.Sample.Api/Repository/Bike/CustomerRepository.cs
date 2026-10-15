using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike;

/// <summary>Newest customers first: the ones registered by the customer-onboarding workflow appear on the first page (and in lookups).</summary>
public class CustomerRepository(BikeStoresContext context) : BaseCrudRepository<Customer, BikeStoresContext>(context)
{
    protected override string? DefaultSortProperty => nameof(Customer.CustomerId);

    protected override bool DefaultSortDescending => true;

    // Lookups use getall, which is capped (MiddtApiOptions.MaxGetAllItems = 1000) – BikeStores has ~1450 customers:
    // without this order the records just created by the workflows would not be selectable.
    public override IQueryable<Customer> GetAll() => base.GetAll().OrderByDescending(e => e.CustomerId);
}
