using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class ProductRepository : BaseCrudRepository<Product, BikeStoresContext>
    {
        public ProductRepository() : base()
        {

        }
        public ProductRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
