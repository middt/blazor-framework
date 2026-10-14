using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Blazor.Web.Security;

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
        if (httpContext.User.FindFirst(MiddtClaimTypes.SessionId)?.Value is { } sessionId)
        {
            if (await tokenStore.GetAsync(sessionId, touch: false, cancellationToken) is { access_token: { } access, refresh_token: { } refresh })
            {
                await tokenService.RevokeAsync(access, refresh, cancellationToken);
            }

            await tokenStore.RemoveAsync(sessionId, cancellationToken);
            LogSignedOut(logger, sessionId[..8]);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionPrefix}… signed out")]
    private static partial void LogSignedOut(ILogger logger, string sessionPrefix);
}
