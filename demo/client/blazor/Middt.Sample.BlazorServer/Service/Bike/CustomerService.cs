using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Client.Security;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.BlazorServer.Service;

public sealed class CustomerService(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<CustomerService> logger)
    : BaseCrudApiClient<Customer>(httpClient, sessionState, tokenService, apiSettings, logger)
{
    protected override string ControllerName => "Customer";
}
