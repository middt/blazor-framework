using Middt.Framework.Common.Database;
using Middt.Template.Api.Model.Database;

namespace Middt.Template.Api.Repository
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
