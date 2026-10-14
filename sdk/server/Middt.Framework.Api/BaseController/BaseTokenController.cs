using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Security;
using Middt.Framework.Api.Swagger;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Enumerations;

namespace Middt.Framework.Api;

/// <summary>
/// Login / refresh / revoke endpoints. Applications implement credential checking (<see cref="CheckUsernamePasswordAsync"/>)
/// and user re-loading (<see cref="LoadUserAsync"/>); token issuing, refresh token rotation, reuse detection and revocation
/// are handled by the framework (<see cref="IJwtTokenGenerator"/>, <see cref="IRefreshTokenStore"/>).
/// </summary>
/// <remarks>
/// <see cref="AllowAnonymousAttribute"/> is applied per action: on the class it would override the <c>[Authorize]</c> of <see cref="Revoke"/>.
/// </remarks>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract partial class BaseTokenController(IJwtTokenGenerator tokenGenerator) : BaseController
{
    private IRefreshTokenStore RefreshTokens => HttpContext.RequestServices.GetRequiredService<IRefreshTokenStore>();

    private ILogger Logger => HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    [HttpPost("[action]")]
    [AllowAnonymous]
    [SwaggerTag(true)]
    [EnableRateLimiting(DependencyInjection.MiddtApiExtensions.TokenRateLimitPolicy)]
    [EndpointSummary("Token servisi")]
    [Description("Kullanıcı adı ve şifre ile access/refresh token üretir.")]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public virtual async Task<ActionResult<TokenResponseModel>> Login([FromBody] LoginRequestModel request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
        {
            return BadRequest(Error("Kullanıcı adı veya şifre alanını boş geçemezsiniz."));
        }

        var login = await CheckUsernamePasswordAsync(request, cancellationToken);
        if (login.UserID is not > 0)
        {
            LogLoginFailed(Logger, request.username, HttpContext.Connection.RemoteIpAddress?.ToString());
            // Same message for unknown user and wrong password (no user enumeration).
            return Unauthorized(Error("Kullanıcı adı veya şifreniz hatalı."));
        }

        LogLoginSucceeded(Logger, login.UserID.Value, HttpContext.Connection.RemoteIpAddress?.ToString());

        var familyExpiresAt = HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().AddSeconds(JwtSettings.RefreshTokenExpires);
        var response = await IssueTokenAsync(login, Guid.NewGuid().ToString("N"), familyExpiresAt, cancellationToken);
        response.listTokenResponseAdditionalDataModel = await AdditionalDataAsync(request, cancellationToken);
        return response;
    }

    [HttpPost("[action]")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<TokenResponseModel>(StatusCodes.Status401Unauthorized)]
    public virtual async Task<ActionResult<TokenResponseModel>> RefreshToken([FromBody] TokenRefreshRequestModel request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.refresh_token) || string.IsNullOrEmpty(request.token))
        {
            return BadRequest(Error("Token bilgilerini boş geçemezsiniz."));
        }

        // The (expired) access token proves which user the refresh token was issued to.
        var principal = await tokenGenerator.GetPrincipalFromExpiredTokenAsync(request.token);
        if (!TryGetUserId(principal, out var userId))
        {
            return Unauthorized(Error("Oturum bilgileriniz geçersiz."));
        }

        var validation = await RefreshTokens.ConsumeAsync(request.refresh_token, cancellationToken);
        var entry = validation.Entry;

        if (validation.Status == RefreshTokenStatus.Reused)
        {
            LogRefreshReuse(Logger, entry!.UserId, entry.FamilyId);
        }

        if (validation.Status != RefreshTokenStatus.Valid || entry is null || entry.UserId != userId)
        {
            if (entry is not null && entry.UserId != userId)
            {
                await RefreshTokens.RevokeFamilyAsync(entry.FamilyId, entry.ExpiresAt, cancellationToken);
            }

            return Unauthorized(Error("Oturumunuzun süresi doldu, lütfen tekrar giriş yapın."));
        }

        // Re-load the user so disabled accounts / changed roles take effect on the next refresh.
        var login = await LoadUserAsync(userId, cancellationToken);
        if (login.UserID is not > 0)
        {
            await RefreshTokens.RevokeFamilyAsync(entry.FamilyId, entry.ExpiresAt, cancellationToken);
            return Unauthorized(Error("Oturumunuzun süresi doldu, lütfen tekrar giriş yapın."));
        }

        return await IssueTokenAsync(login, entry.FamilyId, entry.ExpiresAt, cancellationToken);
    }

    /// <summary>Revokes the refresh token family (logout). Always returns 204 so it cannot be used to probe tokens.</summary>
    [HttpPost("[action]")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public virtual async Task<IActionResult> Revoke([FromBody] TokenRevokeRequestModel request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.refresh_token) &&
            await RefreshTokens.FindAsync(request.refresh_token, cancellationToken) is { } entry &&
            TryGetUserId(User, out var userId) && entry.UserId == userId)
        {
            await RefreshTokens.RevokeFamilyAsync(entry.FamilyId, entry.ExpiresAt, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>Claims embedded in the access token. Override to add application specific claims.</summary>
    protected virtual IEnumerable<Claim> CreateClaims(LoginResponseModel login)
    {
        yield return new Claim(ClaimTypes.Sid, login.UserID!.Value.ToString(CultureInfo.InvariantCulture));
        yield return new Claim(ClaimTypes.NameIdentifier, login.Username ?? string.Empty);
        yield return new Claim(ClaimTypes.Name, login.Username ?? string.Empty);

        if (login.Rol is { } role)
        {
            yield return new Claim(ClaimTypes.Role, role.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Validates credentials. Return a model with <c>UserID &gt; 0</c> on success. Use a constant-time password hash check.</summary>
    protected abstract Task<LoginResponseModel> CheckUsernamePasswordAsync(LoginRequestModel request, CancellationToken cancellationToken);

    /// <summary>Re-loads an existing user during refresh. Return <c>UserID &lt;= 0</c> when the account no longer may sign in.</summary>
    protected abstract Task<LoginResponseModel> LoadUserAsync(long userId, CancellationToken cancellationToken);

    protected virtual Task<List<TokenResponseAdditionalDataModel>?> AdditionalDataAsync(LoginRequestModel request, CancellationToken cancellationToken) =>
        Task.FromResult<List<TokenResponseAdditionalDataModel>?>(null);

    private JwtSettings JwtSettings => HttpContext.RequestServices.GetRequiredService<IOptions<JwtSettings>>().Value;

    private async Task<TokenResponseModel> IssueTokenAsync(LoginResponseModel login, string familyId, DateTimeOffset familyExpiresAt, CancellationToken cancellationToken)
    {
        var token = tokenGenerator.CreateToken(CreateClaims(login));
        token.Username = login.Username;
        token.Result = ResultEnum.Success;

        await RefreshTokens.StoreAsync(token.refresh_token!, new RefreshTokenEntry(login.UserID!.Value, familyId, familyExpiresAt), cancellationToken);
        return token;
    }

    private static bool TryGetUserId(ClaimsPrincipal? principal, out long userId)
    {
        userId = 0;
        return principal?.FindFirst(ClaimTypes.Sid) is { } claim &&
               long.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private static TokenResponseModel Error(params string[] messages) =>
        new() { Result = ResultEnum.Error, MessageList = [.. messages] };

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Login failed for {UserName} from {ClientIp}")]
    private static partial void LogLoginFailed(ILogger logger, string? userName, string? clientIp);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Login succeeded for user {UserId} from {ClientIp}")]
    private static partial void LogLoginSucceeded(ILogger logger, long userId, string? clientIp);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId}; token family {FamilyId} revoked")]
    private static partial void LogRefreshReuse(ILogger logger, long userId, string familyId);
}
