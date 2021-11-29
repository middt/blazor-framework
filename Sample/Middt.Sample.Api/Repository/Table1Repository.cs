using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository
{
    public class Table1Repository : BaseCrudRepository<Table1, Frameworkv2Context>
    {
        public Table1Repository() : base()
        {

        }
        public Table1Repository(Frameworkv2Context context) : base(context)
        {

        }
    }
}
