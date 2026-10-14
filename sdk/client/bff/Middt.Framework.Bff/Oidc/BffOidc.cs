using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Bff.Security;
using Middt.Framework.Client.Security;
using Middt.Framework.Common.Http;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Bff.Oidc;

/// <summary>
/// OIDC mode of the BFF (<see cref="MiddtBffOidcOptions"/>): authorization code flow with PKCE against the identity
/// provider; the callback creates the same server side session as the password login (<see cref="CookieSignInService"/>):
/// a fresh session id, tokens in <see cref="IServerTokenStore"/> with the absolute session expiry, and an HttpOnly cookie
/// that carries only the user's claims and the session id.
/// </summary>
public static partial class BffOidc
{
    /// <summary>Authentication scheme of the identity provider.</summary>
    public const string SchemeName = OpenIdConnectDefaults.AuthenticationScheme;

    /// <summary><see cref="IHttpClientFactory"/> client for discovery, token, userinfo and revocation requests.</summary>
    public const string BackchannelClientName = "middt-bff-oidc-backchannel";

    private static readonly object TokenResponseItem = new();
    private static readonly object IdTokenHintItem = new();

    /// <summary>Registers the OpenID Connect handler and replaces the token service with <see cref="OidcTokenService"/>.</summary>
    public static void Add(AuthenticationBuilder authentication, MiddtBffOidcOptions oidc, string cookieName, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(oidc);
        ArgumentNullException.ThrowIfNull(environment);

        if (!Uri.TryCreate(oidc.Authority, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{MiddtBffOidcOptions.SectionName}:Authority '{oidc.Authority}' must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(oidc.ClientId))
        {
            throw new InvalidOperationException($"{MiddtBffOidcOptions.SectionName}:ClientId is required when {MiddtBffOidcOptions.SectionName}:Authority is set.");
        }

        var services = authentication.Services;
        services.AddSingleton(oidc);
        services.AddHttpClient(BackchannelClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
            .AddHttpMessageHandler(() => new BackchannelRewriteHandler(oidc.Authority, oidc.BackchannelAuthority));

        // Refresh and revocation go to the provider (BffTokenAccessor, Blazor's session state and logout use ITokenService).
        services.RemoveAll<ITokenService>();
        services.AddTransient<ITokenService, OidcTokenService>();

        var secureCookies = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        authentication.AddOpenIdConnect(SchemeName, o =>
        {
            o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.SignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.Authority = oidc.Authority;
            o.ClientId = oidc.ClientId;
            o.ClientSecret = string.IsNullOrEmpty(oidc.ClientSecret) ? null : oidc.ClientSecret;
            o.RequireHttpsMetadata = oidc.RequireHttpsMetadata ?? !environment.IsDevelopment();
            o.ResponseType = OpenIdConnectResponseType.Code;
            // A top-level GET back to the app: works with SameSite=Lax cookies, also over plain http in development.
            o.ResponseMode = OpenIdConnectResponseMode.Query;
            o.UsePkce = true;
            o.PushedAuthorizationBehavior = oidc.UsePushedAuthorization ? PushedAuthorizationBehavior.UseIfAvailable : PushedAuthorizationBehavior.Disable;
            o.MapInboundClaims = false;
            o.GetClaimsFromUserInfoEndpoint = true;
            // Tokens go to the server side store, never into the (cookie) authentication properties.
            o.SaveTokens = false;
            o.DisableTelemetry = true;
            o.CallbackPath = oidc.CallbackPath;
            o.SignedOutCallbackPath = oidc.SignedOutCallbackPath;
            o.SignedOutRedirectUri = oidc.SignedOutRedirectUri;
            o.Scope.Clear();
            foreach (var scope in oidc.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                o.Scope.Add(scope);
            }

            o.TokenValidationParameters.NameClaimType = "name";
            o.TokenValidationParameters.RoleClaimType = "role";
            o.ClaimActions.MapJsonKey("role", "role");

            // Browsers share cookies across ports: prefix with the app's cookie name; Lax suffices for response_mode=query.
            o.CorrelationCookie.Name = cookieName + ".oidc.correlation.";
            o.CorrelationCookie.SameSite = SameSiteMode.Lax;
            o.CorrelationCookie.SecurePolicy = secureCookies;
            o.NonceCookie.Name = cookieName + ".oidc.nonce.";
            o.NonceCookie.SameSite = SameSiteMode.Lax;
            o.NonceCookie.SecurePolicy = secureCookies;

            o.Events.OnTokenValidated = context =>
            {
                context.HttpContext.Items[TokenResponseItem] = context.TokenEndpointResponse;
                return Task.CompletedTask;
            };
            o.Events.OnTicketReceived = CreateSessionAsync;
            o.Events.OnRedirectToIdentityProviderForSignOut = RedirectToEndSessionAsync;
            o.Events.OnRemoteFailure = RemoteFailureAsync;
        });

        // Server-to-server calls through the rewrite handler (internal URL), issuer/metadata URLs stay public.
        services.AddOptions<OpenIdConnectOptions>(SchemeName)
            .Configure<IHttpClientFactory>((o, httpClients) => o.Backchannel = httpClients.CreateClient(BackchannelClientName));
    }

    /// <summary>
    /// Ends the provider session: <c>id_token_hint</c> of the (already removed) server session, then the provider returns
    /// to <see cref="MiddtBffOidcOptions.SignedOutCallbackPath"/> and on to <see cref="MiddtBffOidcOptions.SignedOutRedirectUri"/>.
    /// </summary>
    public static Task SignOutAsync(HttpContext httpContext, string? idTokenHint, string redirectUri)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Items[IdTokenHintItem] = idTokenHint;
        return httpContext.SignOutAsync(SchemeName, new AuthenticationProperties { RedirectUri = redirectUri });
    }

    /// <summary>The server session of a successful sign-in at the provider (replaces the ticket's principal).</summary>
    private static async Task CreateSessionAsync(TicketReceivedContext context)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        if (httpContext.Items[TokenResponseItem] is not OpenIdConnectMessage { AccessToken: { Length: > 0 } accessToken } tokenResponse
            || context.Principal?.Identity is not ClaimsIdentity source)
        {
            context.Fail("The token response contains no access token.");
            return;
        }

        httpContext.Items.Remove(TokenResponseItem);

        // Signing in again on the same browser must not leave the previous session's tokens alive.
        var signIn = services.GetRequiredService<CookieSignInService>();
        var previous = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (previous.Principal?.FindFirst(MiddtClaimTypes.SessionId)?.Value is { } previousSession)
        {
            await signIn.EndSessionAsync(previousSession, httpContext.RequestAborted);
        }

        var identity = CreateCookieIdentity(source, accessToken, out var name);
        var sessionId = Guid.NewGuid().ToString("N");
        identity.AddClaim(new Claim(MiddtClaimTypes.SessionId, sessionId));

        var settings = services.GetRequiredService<IOptions<GeneralSettings>>().Value;
        var expires = services.GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(settings.SessionAbsoluteTimeoutInMinute);
        var tokens = new SaveTokenResponseModel
        {
            Result = ResultEnum.Success,
            access_token = accessToken,
            refresh_token = tokenResponse.RefreshToken,
            expires_in = int.TryParse(tokenResponse.ExpiresIn, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0,
            Username = name,
            expire_datetime = expires,
        };
        OidcTokens.SetIdToken(tokens, tokenResponse.IdToken);
        await services.GetRequiredService<IServerTokenStore>().SetAsync(sessionId, tokens, httpContext.RequestAborted);

        context.Principal = new ClaimsPrincipal(identity);
        var properties = context.Properties ??= new AuthenticationProperties();
        properties.IsPersistent = false;
        properties.ExpiresUtc = expires;
        properties.AllowRefresh = false;
        LogSignedIn(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BffOidc)), sessionId[..8]);
    }

    /// <summary>
    /// Claims of the auth cookie in the framework's <see cref="ClaimTypes"/> (same shape as the password login):
    /// name, roles (identity token / userinfo and the access token), subject and e-mail.
    /// </summary>
    public static ClaimsIdentity CreateCookieIdentity(ClaimsIdentity source, string? accessToken, out string? name)
    {
        ArgumentNullException.ThrowIfNull(source);
        var subject = source.FindFirst("sub")?.Value;
        name = source.FindFirst("name")?.Value ?? source.FindFirst("preferred_username")?.Value ?? source.FindFirst("email")?.Value ?? subject;
        var email = source.FindFirst("email")?.Value;

        var roles = source.FindAll("role").Select(c => c.Value).ToList();
        if (!string.IsNullOrEmpty(accessToken))
        {
            try
            {
                // Received from the token endpoint over the backchannel: only read for its role claims.
                roles.AddRange(new JsonWebToken(accessToken).Claims.Where(c => c.Type == "role").Select(c => c.Value));
            }
            catch (ArgumentException)
            {
                // Reference (non-JWT) access token: roles come from the identity token / userinfo only.
            }
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        if (!string.IsNullOrEmpty(subject))
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        if (!string.IsNullOrEmpty(name))
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
        }

        if (!string.IsNullOrEmpty(email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        }

        foreach (var role in roles.Where(r => r.Length > 0).Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return identity;
    }

    /// <summary>
    /// Navigates to the end-session endpoint with an HTML page (meta refresh) instead of a <c>302</c>: the logout is a form
    /// post, and browsers apply the CSP <c>form-action 'self'</c> to redirects that follow a form submission.
    /// </summary>
    private static async Task RedirectToEndSessionAsync(RedirectContext context)
    {
        if (context.HttpContext.Items[IdTokenHintItem] is string { Length: > 0 } idToken)
        {
            context.ProtocolMessage.IdTokenHint = idToken;
        }

        // The handler adds the state (return URL for the signed-out callback) only after this event: add it here, as it does.
        var properties = context.Properties;
        if (!string.IsNullOrEmpty(context.ProtocolMessage.State))
        {
            properties.Items[OpenIdConnectDefaults.UserstatePropertiesKey] = context.ProtocolMessage.State;
        }

        context.ProtocolMessage.State = context.Options.StateDataFormat.Protect(properties);
        var url = context.ProtocolMessage.CreateLogoutRequestUrl();
        context.HandleResponse();

        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        var encoded = WebUtility.HtmlEncode(url);
        await response.WriteAsync(
            "<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\" /><meta name=\"robots\" content=\"noindex\" />" +
            $"<meta http-equiv=\"refresh\" content=\"0;url={encoded}\" /><title>Çıkış</title>" +
            "<link rel=\"stylesheet\" href=\"/_content/Middt.Framework.Bff/account.css\" /></head>" +
            "<body class=\"middt-account\"><main class=\"middt-account-card\"><h1>Çıkış</h1>" +
            $"<p>Oturumunuz kapatılıyor…</p><p><a id=\"middt-end-session\" href=\"{encoded}\">Devam et</a></p></main></body></html>",
            context.HttpContext.RequestAborted);
    }

    /// <summary>Failed or cancelled sign-in (e.g. consent denied): a short page instead of an exception or a redirect loop.</summary>
    private static async Task RemoteFailureAsync(RemoteFailureContext context)
    {
        LogRemoteFailure(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BffOidc)), context.Failure);
        context.HandleResponse();
        var response = context.Response;
        response.StatusCode = StatusCodes.Status400BadRequest;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        await response.WriteAsync(
            "<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\" /><meta name=\"robots\" content=\"noindex\" /><title>Giriş</title>" +
            "<link rel=\"stylesheet\" href=\"/_content/Middt.Framework.Bff/account.css\" /></head>" +
            "<body class=\"middt-account\"><main class=\"middt-account-card\"><h1>Giriş</h1>" +
            "<p class=\"middt-account-error\" role=\"alert\">Giriş tamamlanamadı.</p><p><a href=\"/\">Tekrar dene</a></p></main></body></html>",
            context.HttpContext.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionPrefix}… signed in at the identity provider")]
    private static partial void LogSignedIn(ILogger logger, string sessionPrefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenID Connect sign-in failed")]
    private static partial void LogRemoteFailure(ILogger logger, Exception? exception);
}
