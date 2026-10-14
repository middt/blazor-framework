using Microsoft.AspNetCore.Authorization;
using Middt.Framework.Common.SignalR;

namespace Middt.Sample.Api;

[Authorize]
public class TestHub : BaseSignalRHub<string>;
