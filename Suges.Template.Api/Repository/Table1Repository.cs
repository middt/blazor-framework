using Suges.Framework.Common.Database;
using Suges.Template.Api.Model.Database;

namespace Suges.Template.Api.Repository
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
