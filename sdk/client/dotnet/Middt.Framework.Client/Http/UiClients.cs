using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Client.Security;
using Middt.Framework.Ui.Model;

namespace Middt.Framework.Client.Http;

/// <summary>Typed client of the layout API mapped by <c>MapMiddtUi</c> (default prefix <c>api/ui</c>).</summary>
public class UiApiClient(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    ILogger<UiApiClient> logger)
    : ApiClientBase(httpClient, sessionState, tokenService, logger)
{
    protected virtual string RoutePrefix => "api/ui";

    public virtual Task<BaseResponseDataModel<List<UiLayoutSummary>>> ListAsync(string? kind = null, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<UiLayoutSummary>>>(HttpMethod.Get, $"{RoutePrefix}/layouts{(kind is null ? null : "?kind=" + Uri.EscapeDataString(kind))}", null, cancellationToken);

    public virtual Task<BaseResponseDataModel<UiLayoutDocument>> GetAsync(string key, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<UiLayoutDocument>>(HttpMethod.Get, $"{RoutePrefix}/layouts/{EscapePathSegment(key, nameof(key))}", null, cancellationToken);

    public virtual Task<BaseResponseDataModel<UiValidationResult>> ValidateAsync(UiLayoutDocument layout, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<UiValidationResult>>(HttpMethod.Post, $"{RoutePrefix}/layouts/validate", layout, cancellationToken);

    /// <summary>Creates or updates <paramref name="layout"/>; <see cref="UiLayoutDocument.Version"/> must be the loaded version (0 for a new layout).</summary>
    public virtual Task<BaseResponseDataModel<UiLayoutDocument>> SaveAsync(UiLayoutDocument layout, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<UiLayoutDocument>>(HttpMethod.Put, $"{RoutePrefix}/layouts/{EscapePathSegment(layout.Key, nameof(layout.Key))}", layout, cancellationToken);

    public virtual Task<BaseResponseModel> DeleteAsync(string key, int? version = null, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseModel>(HttpMethod.Delete, $"{RoutePrefix}/layouts/{EscapePathSegment(key, nameof(key))}{(version is { } v ? "?version=" + v.ToString(CultureInfo.InvariantCulture) : null)}", null, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<UiLayoutRevisionModel>>> RevisionsAsync(string key, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<UiLayoutRevisionModel>>>(HttpMethod.Get, $"{RoutePrefix}/layouts/{EscapePathSegment(key, nameof(key))}/revisions", null, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<UiWidgetSpecModel>>> WidgetsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<UiWidgetSpecModel>>>(HttpMethod.Get, $"{RoutePrefix}/widgets", null, cancellationToken);
}

/// <summary>
/// Untyped client of the CRUD controllers (<c>api/v{version}/{entity}/getitems|getall|getbyid|insert|update|delete</c>) used by
/// layout widgets. Calls run with the signed-in user's token, so the API's authorization applies unchanged.
/// </summary>
public partial class UiDataClient(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    IOptions<ApiSettings> apiSettings,
    ILogger<UiDataClient> logger)
    : ApiClientBase(httpClient, sessionState, tokenService, logger)
{
    public virtual Task<BaseResponseDataModel<List<JsonObject>>> GetItemsAsync(string entity, JsonObject searchRequest, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<JsonObject>>>(HttpMethod.Post, Route(entity, "getitems"), searchRequest, cancellationToken);

    public virtual Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(string entity, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<JsonObject>>>(HttpMethod.Get, Route(entity, "getall"), null, cancellationToken);

    public virtual Task<BaseResponseDataModel<JsonObject>> GetByIdAsync(string entity, string id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<JsonObject>>(HttpMethod.Get, Route(entity, $"getbyid?id={Uri.EscapeDataString(id)}"), null, cancellationToken);

    public virtual Task<BaseResponseDataModel<JsonObject>> InsertAsync(string entity, JsonObject model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<JsonObject>>(HttpMethod.Post, Route(entity, "insert"), model, cancellationToken);

    public virtual Task<BaseResponseDataModel<JsonObject>> UpdateAsync(string entity, JsonObject model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<JsonObject>>(HttpMethod.Post, Route(entity, "update"), model, cancellationToken);

    public virtual Task<BaseResponseDataModel<JsonObject>> DeleteAsync(string entity, JsonObject model, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<JsonObject>>(HttpMethod.Post, Route(entity, "delete"), model, cancellationToken);

    /// <summary>Builds a paged search request (<c>getitems</c> wire format).</summary>
    public static JsonObject SearchRequest(JsonObject filter, int page, int pageSize, string? sortProperty, bool ascending) => new()
    {
        ["currentPage"] = page,
        ["requestItemSize"] = pageSize,
        ["sortProperty"] = sortProperty,
        ["sortReverse"] = ascending, // legacy wire semantics: true = ascending
        ["requestModel"] = filter,
    };

    protected virtual string Route(string entity, string action) =>
        EntityRegex().IsMatch(entity)
            ? $"api/v{apiSettings.Value.Version}/{entity}/{action}"
            : throw new ArgumentException($"Invalid entity name '{entity}'.", nameof(entity));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityRegex();
}
