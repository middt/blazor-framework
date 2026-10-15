using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike;

/// <summary>Newest products first: the ones launched by the product-launch workflow appear on the first page (and in lookups).</summary>
public class ProductRepository(BikeStoresContext context) : BaseCrudRepository<Product, BikeStoresContext>(context)
{
    protected override string? DefaultSortProperty => nameof(Product.ProductId);

    protected override bool DefaultSortDescending => true;

    // Lookups use getall, which is capped (MiddtApiOptions.MaxGetAllItems = 1000) – BikeStores has ~1450 customers:
    // without this order the records just created by the workflows would not be selectable.
    public override IQueryable<Product> GetAll() => base.GetAll().OrderByDescending(e => e.ProductId);
}
