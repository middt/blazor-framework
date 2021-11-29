using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Service;
using Suges.Template.Api.Model.Database;

namespace Suges.Template.Common.Service
{
    public class Table1Service : BaseCrudRefit<ITable1Service, Table1>
    {
        public override string controllerName => "Table1";
        public Table1Service(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }
    }
}