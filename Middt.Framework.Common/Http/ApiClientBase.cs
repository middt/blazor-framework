using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Common.Http;

/// <summary>
/// Base class for typed HTTP clients that talk to a Middt API.
/// Adds the bearer token from <see cref="IBaseSessionState"/>, transparently refreshes an expired
/// token once on <c>401</c>, and maps every failure (network, ProblemDetails, unexpected payload)
/// to a <see cref="BaseResponseModel"/> with <c>Result = Error</c> so callers never see raw exceptions.
/// </summary>
/// <remarks>
/// The token is attached here rather than in a <see cref="DelegatingHandler"/> on purpose: handlers built by
/// <see cref="IHttpClientFactory"/> live in their own DI scope and cannot see the Blazor Server circuit scope
/// that owns <see cref="IBaseSessionState"/>.
/// </remarks>
public abstract class ApiClientBase(
    HttpClient httpClient,
    IBaseSessionState sessionState,
    ITokenService tokenService,
    ILogger logger)
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // Lazy: ConcurrentDictionary.GetOrAdd may run the value factory more than once under contention.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<bool>>> InflightRefreshes = new(StringComparer.Ordinal);

    protected HttpClient HttpClient { get; } = httpClient;

    protected ILogger Logger { get; } = logger;

    protected async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken)
        where TResponse : BaseResponseModel, new()
    {
        try
        {
            var usedToken = await sessionState.Token();
            using var response = await SendWithTokenAsync(method, requestUri, body, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync(usedToken, cancellationToken))
            {
                using var retry = await SendWithTokenAsync(method, requestUri, body, cancellationToken);
                return await ReadResponseAsync<TResponse>(retry, cancellationToken);
            }

            return await ReadResponseAsync<TResponse>(response, cancellationToken);
        }
        // Polly.ExecutionRejectedException: open circuit breaker / pipeline timeout of the resilience handler.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or Polly.ExecutionRejectedException)
        {
            if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            Logger.LogError(ex, "{Method} {Uri} failed", method, requestUri);
            return Failure<TResponse>("Servise erişilemedi. Lütfen daha sonra tekrar deneyin.");
        }
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        var token = await sessionState.Token();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await HttpClient.SendAsync(request, cancellationToken);
    }

    private async Task<bool> TryRefreshTokenAsync(string? usedToken, CancellationToken cancellationToken)
    {
        var token = await sessionState.Token();
        if (!string.IsNullOrEmpty(token) && token != usedToken)
        {
            // Another request of this session already refreshed the token; just retry with it.
            return true;
        }

        var refreshToken = await sessionState.RefreshToken();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        // Refresh tokens are single use (rotation + reuse detection on the API): concurrent 401s of the same
        // session must share one refresh call, otherwise the second call would revoke the whole session.
        var inflight = InflightRefreshes.GetOrAdd(refreshToken, _ => new Lazy<Task<bool>>(() => RefreshAsync(token, refreshToken)));
        var refresh = inflight.Value;
        try
        {
            return await refresh.WaitAsync(cancellationToken);
        }
        finally
        {
            if (refresh.IsCompleted)
            {
                InflightRefreshes.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(refreshToken, inflight));
            }
        }
    }

    private async Task<bool> RefreshAsync(string token, string refreshToken)
    {
        // A request that read the session before a concurrent refresh completed (and removed its in-flight entry) would
        // otherwise replay the already rotated refresh token, which the API treats as reuse and revokes the session.
        var current = await sessionState.RefreshToken();
        if (current != refreshToken)
        {
            return !string.IsNullOrEmpty(current);
        }

        var result = await tokenService.RefreshTokenAsync(
            new TokenRefreshRequestModel { token = token, refresh_token = refreshToken },
            CancellationToken.None);

        if (!result.IsSuccess)
        {
            Logger.LogWarning("Token refresh failed, signing the session out: {Error}", result.ErrorText);
            await sessionState.ClearToken();
            return false;
        }

        await sessionState.SetToken(result);
        return true;
    }

    private async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
        where TResponse : BaseResponseModel, new()
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
                ?? Failure<TResponse>("Servis boş yanıt döndü.");
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);
        Logger.LogWarning("{Uri} returned {StatusCode}: {Problem}", response.RequestMessage?.RequestUri, (int)response.StatusCode, problem?.Detail ?? problem?.Title);

        var messages = new List<string>();
        if (problem is ValidationProblemDetails validation)
        {
            messages.AddRange(validation.Errors.SelectMany(e => e.Value));
        }

        if (messages.Count == 0)
        {
            messages.Add(problem?.Detail ?? problem?.Title ?? response.ReasonPhrase ?? response.StatusCode.ToString());
        }

        return Failure<TResponse>([.. messages]);
    }

    private static async Task<ProblemDetails?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json"))
        {
            return null;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(SerializerOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static TResponse Failure<TResponse>(params string[] messages)
        where TResponse : BaseResponseModel, new() =>
        new() { Result = ResultEnum.Error, MessageList = [.. messages] };
}
