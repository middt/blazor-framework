using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class CustomerRepository : BaseCrudRepository<Customer, BikeStoresContext>
    {
        public CustomerRepository() : base()
        {

        }
        public CustomerRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
