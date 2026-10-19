using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Middt.Framework.Client.Http;
using Middt.Framework.Client.Security;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Tests.Api;

namespace Middt.Framework.Tests.Client;

public sealed class ItemsApiClient(HttpClient httpClient, IBaseSessionState sessionState, ITokenService tokenService, string controllerName = "items")
    : ApiClientBase(httpClient, sessionState, tokenService, NullLogger.Instance)
{
    private string Route(string action) => $"api/v1/{EscapePathSegment(controllerName, nameof(controllerName))}/{action}";

    public Task<BaseResponseDataModel<List<ApiItem>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<ApiItem>>>(HttpMethod.Get, Route("getall"), null, cancellationToken);

    public Task<BaseResponseDataModel<ApiItem>> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<ApiItem>>(
            HttpMethod.Get,
            $"{Route("getbyid")}?id={Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty)}",
            null,
            cancellationToken);

    public Task<BaseResponseDataModel<List<ApiItem>>> GetItemsAsync(object request, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<List<ApiItem>>>(HttpMethod.Post, Route("getitems"), request, cancellationToken);

    public Task<BaseResponseDataModel<ApiItem>> InsertAsync(ApiItem item, CancellationToken cancellationToken = default) =>
        SendAsync<BaseResponseDataModel<ApiItem>>(HttpMethod.Post, Route("insert"), item, cancellationToken);
}

public sealed class FakeSessionState(string token = "", string refreshToken = "") : IBaseSessionState
{
    public string CurrentToken { get; private set; } = token;

    public string CurrentRefreshToken { get; private set; } = refreshToken;

    public Task<string> Token() => Task.FromResult(CurrentToken);

    public Task<string> RefreshToken() => Task.FromResult(CurrentRefreshToken);

    public Task<int> ExpiresIn() => Task.FromResult(600);

    public Task SetToken(TokenResponseModel tokenResponseModel)
    {
        CurrentToken = tokenResponseModel.access_token ?? string.Empty;
        CurrentRefreshToken = tokenResponseModel.refresh_token ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task ClearToken()
    {
        CurrentToken = CurrentRefreshToken = string.Empty;
        return Task.CompletedTask;
    }
}

public sealed class FakeTokenService(Func<TokenRefreshRequestModel, CancellationToken, Task<TokenResponseModel>> refresh) : ITokenService
{
    private int refreshCalls;

    public FakeTokenService(Func<TokenRefreshRequestModel, TokenResponseModel> refresh)
        : this((request, _) => Task.FromResult(refresh(request)))
    {
    }

    /// <summary>Login behaviour; <c>null</c> = not supported.</summary>
    public Func<LoginRequestModel, TokenResponseModel>? Login { get; init; }

    public List<TokenRefreshRequestModel> RefreshRequests { get; } = [];

    public List<LoginRequestModel> LoginRequests { get; } = [];

    public List<(string AccessToken, string RefreshToken)> RevokeRequests { get; } = [];

    public int RefreshCalls => Volatile.Read(ref refreshCalls);

    public Task<TokenResponseModel> LoginAsync(LoginRequestModel request, CancellationToken cancellationToken = default)
    {
        if (Login is null)
        {
            throw new NotSupportedException();
        }

        lock (LoginRequests)
        {
            LoginRequests.Add(request);
        }

        return Task.FromResult(Login(request));
    }

    public Task<TokenResponseModel> RefreshTokenAsync(TokenRefreshRequestModel request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref refreshCalls);
        lock (RefreshRequests)
        {
            RefreshRequests.Add(request);
        }

        return refresh(request, cancellationToken);
    }

    public Task RevokeAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        lock (RevokeRequests)
        {
            RevokeRequests.Add((accessToken, refreshToken));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Session state that counts calls and can be shared by concurrent requests.</summary>
public sealed class CountingSessionState(string token, string refreshToken) : IBaseSessionState
{
    private readonly Lock gate = new();
    private string token = token;
    private string refreshToken = refreshToken;
    private int clearCalls;

    public int ClearCalls => Volatile.Read(ref clearCalls);

    public Task<string> Token()
    {
        lock (gate)
        {
            return Task.FromResult(token);
        }
    }

    public Task<string> RefreshToken()
    {
        lock (gate)
        {
            return Task.FromResult(refreshToken);
        }
    }

    public Task<int> ExpiresIn() => Task.FromResult(600);

    public Task SetToken(TokenResponseModel tokenResponseModel)
    {
        lock (gate)
        {
            token = tokenResponseModel.access_token ?? string.Empty;
            refreshToken = tokenResponseModel.refresh_token ?? string.Empty;
        }

        return Task.CompletedTask;
    }

    public Task ClearToken()
    {
        Interlocked.Increment(ref clearCalls);
        lock (gate)
        {
            token = refreshToken = string.Empty;
        }

        return Task.CompletedTask;
    }
}

/// <summary>Records every request (with its bearer token) and answers through a delegate.</summary>
public sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public List<(HttpMethod Method, string Uri, string? Bearer, string? Body)> Requests { get; } = [];

    /// <summary>Request headers of every request (same order as <see cref="Requests"/>).</summary>
    public List<Dictionary<string, string>> Headers { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter, body));
            Headers.Add(request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
        }

        var response = await respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://localhost/") };

    public static HttpResponseMessage Json<T>(HttpStatusCode status, T value, string mediaType = "application/json") =>
        new(status) { Content = JsonContent.Create(value, mediaType: new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType), options: JsonSerializerOptions.Web) };
}

internal static class NullLoggers
{
    public static ILogger<T> For<T>() => NullLogger<T>.Instance;
}
