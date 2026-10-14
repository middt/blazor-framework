using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Client.Security;

namespace Middt.Framework.Client.Http;

/// <summary>Typed client for a <c>BaseCrudController</c>.</summary>
public abstract class BaseCrudApiClient<TModel>(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger logger)
    : BaseListApiClient<TModel>(httpClient, sessionState, tokenService, apiSettings, logger), ICrudApiClient<TModel>
    where TModel : new()
{
    public virtual Task<BaseResponseDataModel<TModel>> InsertAsync(TModel model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<TModel>>(HttpMethod.Post, Route("insert"), model, cancellationToken);

    public virtual Task<BaseResponseDataModel<TModel>> UpdateAsync(TModel model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<TModel>>(HttpMethod.Post, Route("update"), model, cancellationToken);

    public virtual Task<BaseResponseDataModel<TModel>> DeleteAsync(TModel model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<TModel>>(HttpMethod.Post, Route("delete"), model, cancellationToken);
}
