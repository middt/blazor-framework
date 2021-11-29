using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Service;
using Middt.Template.Api.Model.Database;

namespace Middt.Template.Common.Service
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