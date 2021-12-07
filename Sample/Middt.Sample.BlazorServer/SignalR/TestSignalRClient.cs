using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.SignalR;

namespace Middt.Sample.BlazorServer.SignalR
{
    public class TestSignalRClient : BaseSignalRClient<string>
    {
        public TestSignalRClient(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {
        }
        public override string hubName => "/hubs/testhub";
    }
}
