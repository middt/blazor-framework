using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Security;
using Middt.Framework.Common.SignalR;
using Middt.Framework.Client.SignalR;

namespace Middt.Sample.BlazorServer.SignalR;

public sealed class TestSignalRClient(IOptions<ApiSettings> apiSettings, IBaseSessionState sessionState, ILogger<TestSignalRClient> logger)
    : BaseSignalRClient<string>(apiSettings, sessionState, logger)
{
    public override string HubName => "/hubs/testhub";
}
