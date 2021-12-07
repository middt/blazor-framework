using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Service;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Common.Service
{
    public class StoreService : BaseCrudRefit<IStoreService, Store>
    {
        public override string controllerName => "Store";
        public StoreService(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }
    }
}