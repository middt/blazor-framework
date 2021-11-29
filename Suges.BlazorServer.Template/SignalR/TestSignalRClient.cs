using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.SignalR;

namespace Suges.Template.BlazorServer.SignalR
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
