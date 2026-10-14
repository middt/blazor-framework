using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Http;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Client.Security;

/// <summary>
/// Typed client for <c>BaseTokenController</c> (login / refresh). It deliberately does not derive from
/// <see cref="ApiClientBase"/>: token endpoints are anonymous and must not trigger a refresh loop.
/// </summary>
public class BaseTokenService(HttpClient httpClient, IOptions<ApiSettings> apiSettings, ILogger<BaseTokenService> logger) : ITokenService
{
    protected virtual string ControllerName => "token";

    public Task<TokenResponseModel> LoginAsync(LoginRequestModel request, CancellationToken cancellationToken = default) =>
        PostAsync("login", request, cancellationToken);

    public Task<TokenResponseModel> RefreshTokenAsync(TokenRefreshRequestModel request, CancellationToken cancellationToken = default) =>
        PostAsync("refreshtoken", request, cancellationToken);

    public async Task RevokeAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var uri = $"api/v{apiSettings.Value.Version}/{ControllerName}/revoke";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new TokenRevokeRequestModel { refresh_token = refreshToken }, options: ApiClientBase.SerializerOptions),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Refresh token revocation returned {StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Timeouts (TaskCanceledException), resilience rejections (Polly) and network errors must not break sign-out.
            logger.LogWarning(ex, "Refresh token revocation failed");
        }
    }

    private async Task<TokenResponseModel> PostAsync<TRequest>(string action, TRequest request, CancellationToken cancellationToken)
    {
        var uri = $"api/v{apiSettings.Value.Version}/{ControllerName}/{action}";
        try
        {
            using var response = await httpClient.PostAsJsonAsync(uri, request, ApiClientBase.SerializerOptions, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<TokenResponseModel>(ApiClientBase.SerializerOptions, cancellationToken);
            if (response.IsSuccessStatusCode && result is not null)
            {
                return result;
            }

            return new TokenResponseModel
            {
                Result = ResultEnum.Error,
                MessageList = result?.MessageList is { Count: > 0 } messages ? messages : [response.ReasonPhrase ?? "Giriş başarısız."],
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or Polly.ExecutionRejectedException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogError(ex, "Token request {Uri} failed", uri);
            return new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["Kimlik doğrulama servisine erişilemedi."] };
        }
    }
}
