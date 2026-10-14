using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Http;
using Middt.Framework.Common.Security;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Common.Service;

public sealed class ViewOrderService(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<ViewOrderService> logger)
    : BaseCrudApiClient<ViewOrder>(httpClient, sessionState, tokenService, apiSettings, logger)
{
    protected override string ControllerName => "ViewOrder";
}
