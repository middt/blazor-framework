using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;

namespace Middt.Framework.Common.Http;

/// <summary>
/// Typed client for a <c>BaseListController</c>. Derive and set <see cref="ControllerName"/>:
/// <code>public sealed class CategoryService(...) : BaseCrudApiClient&lt;Category&gt;(...) { protected override string ControllerName => "Category"; }</code>
/// </summary>
public abstract class BaseListApiClient<TModel>(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger logger)
    : ApiClientBase(httpClient, sessionState, tokenService, logger), IListApiClient<TModel>
    where TModel : new()
{
    protected abstract string ControllerName { get; }

    /// <summary>API version used in the route (<c>api/v{version}/{controller}</c>).</summary>
    protected virtual string Version => apiSettings.Value.Version;

    protected string Route(string action) => $"api/v{Version}/{ControllerName}/{action}";

    public virtual Task<BaseResponseDataModel<TModel>> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<TModel>>(HttpMethod.Get, Route($"getbyid?id={id}"), null, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<TModel>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<TModel>>>(HttpMethod.Get, Route("getall"), null, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<TModel>>> GetItemsAsync(BaseSearchRequestModel<TModel> model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<TModel>>>(HttpMethod.Post, Route("getitems"), model, cancellationToken);
}
