using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository
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
