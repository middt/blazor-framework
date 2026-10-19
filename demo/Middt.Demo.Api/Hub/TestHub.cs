using Microsoft.AspNetCore.Authorization;
using Middt.Framework.Common.SignalR;

namespace Middt.Demo.Api;

[Authorize]
public class TestHub : BaseSignalRHub<string>;
