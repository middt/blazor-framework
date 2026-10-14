using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Client.Security;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Common.Service;

public sealed class CategoryService(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<CategoryService> logger)
    : BaseCrudApiClient<Category>(httpClient, sessionState, tokenService, apiSettings, logger)
{
    protected override string ControllerName => "Category";
}
