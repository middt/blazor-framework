using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike;

/// <summary><c>viewOrders</c> is a keyless view: page it by the newest order first (stable ORDER BY).</summary>
public class ViewOrderRepository(BikeStoresContext context) : BaseCrudRepository<ViewOrder, BikeStoresContext>(context)
{
    protected override string? DefaultSortProperty => nameof(ViewOrder.OrderId);

    protected override bool DefaultSortDescending => true;
}
