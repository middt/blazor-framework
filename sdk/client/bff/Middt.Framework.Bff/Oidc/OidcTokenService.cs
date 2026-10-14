using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Bff.Oidc;

/// <summary>
/// <see cref="ITokenService"/> of the BFF's OIDC mode: refreshes and revokes tokens at the identity provider (endpoints
/// from its discovery document, fetched through the backchannel), authenticating as the confidential client. Password
/// login is not supported – users sign in at the provider (<c>/Account/Login</c> challenges it).
/// </summary>
public sealed partial class OidcTokenService(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    MiddtBffOidcOptions options,
    ILogger<OidcTokenService> logger) : ITokenService
{
    private const string RefreshFailed = "Oturum yenilenemedi.";

    public Task<TokenResponseModel> LoginAsync(LoginRequestModel request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["Giriş kimlik sağlayıcısı üzerinden yapılır."] });

    public async Task<TokenResponseModel> RefreshTokenAsync(TokenRefreshRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.refresh_token))
        {
            return Failure(RefreshFailed);
        }

        try
        {
            var configuration = await GetConfigurationAsync(cancellationToken);
            using var content = new FormUrlEncodedContent(WithClient(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", request.refresh_token),
            ]));
            using var client = CreateClient();
            using var response = await client.PostAsync(new Uri(configuration.TokenEndpoint), content, cancellationToken);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = json.RootElement;

            if (!response.IsSuccessStatusCode || root.ValueKind != JsonValueKind.Object || String(root, "access_token") is not { Length: > 0 } accessToken)
            {
                LogRefreshRejected(logger, (int)response.StatusCode, root.ValueKind == JsonValueKind.Object ? String(root, "error") : null);
                return Failure(RefreshFailed);
            }

            var result = new TokenResponseModel
            {
                Result = ResultEnum.Success,
                access_token = accessToken,
                // Providers without refresh token rotation return no new refresh token: keep the current one.
                refresh_token = String(root, "refresh_token") ?? request.refresh_token,
                expires_in = root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number ? expires.GetInt32()
                    : int.TryParse(String(root, "expires_in"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0,
            };
            OidcTokens.SetIdToken(result, String(root, "id_token"));
            return result;
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            LogRequestFailed(logger, ex, "refresh");
            return Failure("Kimlik doğrulama servisine erişilemedi.");
        }
    }

    public async Task RevokeAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        try
        {
            var configuration = await GetConfigurationAsync(cancellationToken);
            var endpoint = RevocationEndpoint(configuration);
            if (endpoint is null)
            {
                LogNoRevocationEndpoint(logger);
                return;
            }

            using var content = new FormUrlEncodedContent(WithClient(
            [
                new("token", refreshToken),
                new("token_type_hint", "refresh_token"),
            ]));
            using var client = CreateClient();
            using var response = await client.PostAsync(new Uri(endpoint), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRevocationRejected(logger, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            // Sign-out must not fail because the provider is unreachable.
            LogRequestFailed(logger, ex, "revocation");
        }
    }

    /// <summary><c>revocation_endpoint</c> of the discovery document (RFC 7009 / RFC 8414), or <c>null</c>.</summary>
    public static string? RevocationEndpoint(OpenIdConnectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.IsNullOrEmpty(configuration.RevocationEndpoint))
        {
            return configuration.RevocationEndpoint;
        }

        return configuration.AdditionalData.TryGetValue("revocation_endpoint", out var value) ? value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        } : null;
    }

    private HttpClient CreateClient() => httpClientFactory.CreateClient(BffOidc.BackchannelClientName);

    private Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        // The handler's configuration manager (post-configured with the backchannel): one cached discovery document.
        var manager = oidcOptions.Get(BffOidc.SchemeName).ConfigurationManager
            ?? throw new InvalidOperationException("The OpenID Connect configuration manager is not initialised.");
        return manager.GetConfigurationAsync(cancellationToken);
    }

    private List<KeyValuePair<string, string>> WithClient(List<KeyValuePair<string, string>> fields)
    {
        fields.Add(new("client_id", options.ClientId ?? string.Empty));
        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            fields.Add(new("client_secret", options.ClientSecret));
        }

        return fields;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static TokenResponseModel Failure(string message) => new() { Result = ResultEnum.Error, MessageList = [message] };

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or JsonException or InvalidOperationException or IOException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token refresh at the identity provider returned {StatusCode} ({Error})")]
    private static partial void LogRefreshRejected(ILogger logger, int statusCode, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token revocation at the identity provider returned {StatusCode}")]
    private static partial void LogRevocationRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The identity provider advertises no revocation endpoint; refresh token not revoked")]
    private static partial void LogNoRevocationEndpoint(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Identity provider {Operation} request failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string operation);
}
