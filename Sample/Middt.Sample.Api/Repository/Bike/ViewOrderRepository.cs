using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class ViewOrderRepository : BaseCrudRepository<ViewOrder, BikeStoresContext>
    {
        public ViewOrderRepository() : base()
        {

        }
        public ViewOrderRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
