using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class StoreRepository : BaseCrudRepository<Store, BikeStoresContext>
    {
        public StoreRepository() : base()
        {

        }
        public StoreRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
