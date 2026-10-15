using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike;

/// <summary>
/// Stocks (key store + product). Lists project the store and product names into the row, so grids show names without
/// loading the navigation graphs; filters on <c>storeId</c> / <c>productId</c> still translate to SQL.
/// </summary>
public class StockRepository(BikeStoresContext context) : BaseCrudRepository<Stock, BikeStoresContext>(context)
{
    public override IQueryable<Stock> GetAll() =>
        Entities.AsNoTracking().Select(s => new Stock
        {
            StoreId = s.StoreId,
            ProductId = s.ProductId,
            Quantity = s.Quantity,
            StoreName = s.Store.StoreName,
            ProductName = s.Product.ProductName,
        });
}
