using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Client.Security;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Bff.Security;

/// <summary>
/// Signs a browser in/out: obtains API tokens, keeps them in <see cref="IServerTokenStore"/> and issues an HttpOnly
/// auth cookie that only carries the user's claims and a fresh session id (prevents session fixation).
/// </summary>
public sealed partial class CookieSignInService(
    ITokenService tokenService,
    IServerTokenStore tokenStore,
    IOptions<GeneralSettings> settings,
    TimeProvider timeProvider,
    ILogger<CookieSignInService> logger)
{
    // Registered JWT claims are meaningless inside the cookie.
    private static readonly HashSet<string> JwtOnlyClaims = ["exp", "nbf", "iat", "iss", "aud", "jti"];

    public async Task<TokenResponseModel> SignInAsync(HttpContext httpContext, string username, string password, CancellationToken cancellationToken = default)
    {
        var token = await tokenService.LoginAsync(new LoginRequestModel { username = username, password = password }, cancellationToken);
        if (!token.IsSuccess || string.IsNullOrEmpty(token.access_token))
        {
            return token;
        }

        // Signing in again (e.g. another user on the same browser) must not leave the previous session's tokens alive.
        if (httpContext.User.Identity?.IsAuthenticated == true && httpContext.User.FindFirst(MiddtClaimTypes.SessionId)?.Value is { } previous)
        {
            await EndSessionAsync(previous, cancellationToken);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var expires = timeProvider.GetUtcNow().AddMinutes(settings.Value.SessionAbsoluteTimeoutInMinute);
        await tokenStore.SetAsync(sessionId, new SaveTokenResponseModel(token) { expire_datetime = expires }, cancellationToken);

        var jwtIdentity = JwtClaimsReader.ReadIdentity(token.access_token);
        var identity = new ClaimsIdentity(
            jwtIdentity.Claims.Where(c => !JwtOnlyClaims.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value)),
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        identity.AddClaim(new Claim(MiddtClaimTypes.SessionId, sessionId));

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires, AllowRefresh = false });

        return token;
    }

    public async Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (httpContext.User.FindFirst(MiddtClaimTypes.SessionId)?.Value is { } sessionId)
        {
            await EndSessionAsync(sessionId, cancellationToken);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>The identity token of the current session (OIDC mode), used as <c>id_token_hint</c> at logout.</summary>
    public async Task<string?> GetIdTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.User.FindFirst(MiddtClaimTypes.SessionId)?.Value is { } sessionId
            ? Oidc.OidcTokens.GetIdToken(await tokenStore.GetAsync(sessionId, touch: false, cancellationToken))
            : null;
    }

    /// <summary>
    /// Revokes the session's refresh token (at the API, or at the identity provider in OIDC mode) and removes the session
    /// from the store.
    /// </summary>
    public async Task EndSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        if (await tokenStore.GetAsync(sessionId, touch: false, cancellationToken) is { access_token: { } access, refresh_token: { } refresh })
        {
            await tokenService.RevokeAsync(access, refresh, cancellationToken);
        }

        await tokenStore.RemoveAsync(sessionId, cancellationToken);
        LogSignedOut(logger, sessionId[..Math.Min(8, sessionId.Length)]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionPrefix}… signed out")]
    private static partial void LogSignedOut(ILogger logger, string sessionPrefix);
}
