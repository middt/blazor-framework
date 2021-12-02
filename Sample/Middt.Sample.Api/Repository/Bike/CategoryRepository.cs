using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike
{
    public class CategoryRepository : BaseCrudRepository<Category, BikeStoresContext>
    {
        public CategoryRepository() : base()
        {

        }
        public CategoryRepository(BikeStoresContext context) : base(context)
        {

        }
    }
}
