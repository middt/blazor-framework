using Middt.Framework.Common.Database;
using Middt.Template.Api.Model.Database;

namespace Middt.Template.Api.Repository
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
