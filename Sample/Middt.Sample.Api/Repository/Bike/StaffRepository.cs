using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class StaffRepository : BaseCrudRepository<Staff, BikeStoresContext>
    {
        public StaffRepository() : base()
        {

        }
        public StaffRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
