using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Http;
using Middt.Framework.Common.Security;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Common.Service;

public sealed class StoreService(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<StoreService> logger)
    : BaseCrudApiClient<Store>(httpClient, sessionState, tokenService, apiSettings, logger)
{
    protected override string ControllerName => "Store";
}
