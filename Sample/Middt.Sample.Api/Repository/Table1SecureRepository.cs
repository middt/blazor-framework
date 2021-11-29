using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository
{
    public class Table1SecureRepository : BaseCrudRepository<Table1, Frameworkv2Context>
    {
        public Table1SecureRepository() : base()
        {

        }
        public Table1SecureRepository(Frameworkv2Context context) : base(context)
        {

        }
    }
}
