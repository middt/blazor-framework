using Suges.Framework.Common.Database;
using Suges.Template.Api.Model.Database;

namespace Suges.Template.Api.Repository
{
    public class TableHistoryRepository : BaseCrudRepository<TableHistory, Frameworkv2Context>
    {
        public TableHistoryRepository() : base()
        {

        }
        public TableHistoryRepository(Frameworkv2Context context) : base(context)
        {

        }
    }
}
