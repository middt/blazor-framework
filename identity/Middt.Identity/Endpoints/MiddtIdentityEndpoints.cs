using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Middt.Identity.Journeys;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity;

/// <summary>
/// Protocol endpoints handled by the application (OpenIddict passthrough) and the journey completion endpoint.
/// Requires <c>app.UseAuthentication()</c> before them (the OpenIddict server runs inside the authentication middleware).
/// </summary>
public static class MiddtIdentityEndpoints
{
    public const string AuthorizePath = "connect/authorize";
    public const string PushedAuthorizationPath = "connect/par";
    public const string DeviceAuthorizationPath = "connect/device";
    public const string EndUserVerificationPath = "connect/verify";
    public const string TokenPath = "connect/token";
    public const string UserInfoPath = "connect/userinfo";
    public const string EndSessionPath = "connect/logout";
    public const string RevocationPath = "connect/revoke";
    public const string IntrospectionPath = "connect/introspect";

    /// <summary>Added to the authorize URL after the user denied consent; the authorize endpoint then answers <c>access_denied</c>.</summary>
    public const string ConsentDeniedParameter = "middt_consent_denied";

    /// <summary>
    /// Maps <c>/connect/authorize</c>, <c>/connect/verify</c>, <c>/connect/token</c>, <c>/connect/userinfo</c>, <c>/connect/logout</c>
    /// (passthrough parts of the protocol; discovery, keys, PAR, device authorization, revocation and introspection are answered by OpenIddict itself)
    /// and <c>GET /account/journey/{id}/complete</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapMiddtIdentity(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMethods("/" + AuthorizePath, [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription().DisableAntiforgery();
        endpoints.MapMethods("/" + EndUserVerificationPath, [HttpMethods.Get, HttpMethods.Post], (Delegate)VerifyAsync).ExcludeFromDescription().DisableAntiforgery();
        endpoints.MapPost("/" + TokenPath, (Delegate)TokenAsync).ExcludeFromDescription().DisableAntiforgery();
        endpoints.MapMethods("/" + UserInfoPath, [HttpMethods.Get, HttpMethods.Post], (Delegate)UserInfoAsync).ExcludeFromDescription().DisableAntiforgery();
        endpoints.MapMethods("/" + EndSessionPath, [HttpMethods.Get, HttpMethods.Post], (Delegate)EndSessionAsync).ExcludeFromDescription().DisableAntiforgery();
        endpoints.MapGet("/account/journey/{id:guid}/complete", (Delegate)CompleteJourneyAsync).ExcludeFromDescription();
        return endpoints;
    }

    // ───────────────────────────── authorize ─────────────────────────────

    private static async Task<IResult> AuthorizeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
        var journeys = services.GetRequiredService<IAuthJourneyService>();
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
        var time = services.GetRequiredService<TimeProvider>();

        var session = await context.AuthenticateAsync(MiddtIdentityDefaults.CookieScheme);
        var user = session.Succeeded ? await MiddtIdentitySession.GetValidUserAsync(users, session.Principal) : null;
        var authTime = MiddtIdentitySession.GetAuthenticationTime(session.Principal);
        var tooOld = request.MaxAge is { } maxAge && (authTime is null || time.GetUtcNow() - authTime > TimeSpan.FromSeconds(maxAge));
        var scopes = request.GetScopes();

        // The journey returns to this request without "prompt" (no login/consent loop) and without the denial marker.
        var returnUrl = AuthorizeUrl(context);

        if (user is null || request.HasPromptValue(PromptValues.Login) || tooOld)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.LoginRequired, "Kullanıcı oturumu yok.");
            }

            var handle = await journeys.StartAsync(AuthJourneys.Login,
                new AuthJourneyStart { ReturnUrl = returnUrl, ClientId = request.ClientId, Scopes = [.. scopes] }, cancellationToken);
            AuthJourneyCookie.Write(context, handle);
            return Results.Redirect(MiddtIdentityDefaults.JourneyPath(handle.Id));
        }

        if (request.GetParameter(ConsentDeniedParameter) is not null)
        {
            return Forbid(Errors.AccessDenied, "Kullanıcı izin vermedi.");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");
        var applicationId = await applications.GetIdAsync(application, cancellationToken) ?? string.Empty;
        var existing = new List<object>();
        await foreach (var authorization in authorizations.FindAsync(user.Id, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
        {
            existing.Add(authorization);
        }

        var consentType = await applications.GetConsentTypeAsync(application, cancellationToken);
        if (consentType == ConsentTypes.External && existing.Count == 0)
        {
            return Forbid(Errors.ConsentRequired, "Bu uygulama için yönetici onayı gerekiyor.");
        }

        // Explicit (and systematic, treated as explicit): ask once, or again with prompt=consent.
        if (consentType is ConsentTypes.Explicit or ConsentTypes.Systematic && (existing.Count == 0 || request.HasPromptValue(PromptValues.Consent)))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.ConsentRequired, "Kullanıcı izni gerekiyor.");
            }

            var handle = await journeys.StartAsync(AuthJourneys.Login, new AuthJourneyStart
            {
                ReturnUrl = returnUrl,
                ClientId = request.ClientId,
                Scopes = [.. scopes],
                UserId = user.Id,
                Data = new System.Text.Json.Nodes.JsonObject { [JourneyData.ForceConsent] = request.HasPromptValue(PromptValues.Consent) },
            }, cancellationToken);
            AuthJourneyCookie.Write(context, handle);
            return Results.Redirect(MiddtIdentityDefaults.JourneyPath(handle.Id));
        }

        var identity = await CreateIdentityAsync(services, user, scopes, cancellationToken);
        if (authTime is { } authenticatedAt)
        {
            identity.SetClaim(Claims.AuthenticationTime, authenticatedAt.ToUnixTimeSeconds());
        }

        var permanent = existing.LastOrDefault()
            ?? await authorizations.CreateAsync(identity, user.Id, applicationId, AuthorizationTypes.Permanent, scopes, cancellationToken);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(permanent, cancellationToken));
        identity.SetDestinations(GetDestinations);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // ───────────────────────────── device verify ─────────────────────────────

    private static async Task<IResult> VerifyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
        var journeys = services.GetRequiredService<IAuthJourneyService>();

        var session = await context.AuthenticateAsync(MiddtIdentityDefaults.CookieScheme);
        var user = session.Succeeded ? await MiddtIdentitySession.GetValidUserAsync(users, session.Principal) : null;
        if (user is null)
        {
            var returnUrl = HttpMethods.IsGet(context.Request.Method)
                ? AuthorizeUrl(context)
                : MiddtIdentityDefaults.DevicePath;
            var handle = await journeys.StartAsync(AuthJourneys.Login, new AuthJourneyStart { ReturnUrl = returnUrl }, cancellationToken);
            AuthJourneyCookie.Write(context, handle);
            return Results.Redirect(MiddtIdentityDefaults.JourneyPath(handle.Id));
        }

        if (HttpMethods.IsGet(context.Request.Method))
        {
            if (string.IsNullOrWhiteSpace(request.UserCode))
            {
                return Results.Redirect(MiddtIdentityDefaults.DevicePath);
            }

            var check = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (!check.Succeeded || check.Principal is null)
            {
                return Results.Redirect(MiddtIdentityDefaults.DevicePath + "?error=invalid_code");
            }

            return Results.Redirect($"{MiddtIdentityDefaults.DevicePath}?user_code={Uri.EscapeDataString(request.UserCode)}");
        }

        if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var fetchSite)
            && !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            return Results.Redirect(MiddtIdentityDefaults.DevicePath + "?error=invalid_code");
        }

        var decision = context.Request.HasFormContentType ? context.Request.Form["decision"].ToString() : null;
        if (string.Equals(decision, "deny", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid(
                new AuthenticationProperties { RedirectUri = MiddtIdentityDefaults.DevicePath + "?status=denied" },
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var scopes = result.Principal.GetScopes();
        var identity = await CreateIdentityAsync(services, user, scopes, cancellationToken);
        if (MiddtIdentitySession.GetAuthenticationTime(session.Principal) is { } authenticatedAt)
        {
            identity.SetClaim(Claims.AuthenticationTime, authenticatedAt.ToUnixTimeSeconds());
        }

        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
        if (result.Principal.GetClaim(Claims.ClientId) is { Length: > 0 } clientId
            && await applications.FindByClientIdAsync(clientId, cancellationToken) is { } application)
        {
            var applicationId = await applications.GetIdAsync(application, cancellationToken) ?? string.Empty;
            object? permanent = null;
            await foreach (var authorization in authorizations.FindAsync(user.Id, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
            {
                permanent = authorization;
            }

            permanent ??= await authorizations.CreateAsync(identity, user.Id, applicationId, AuthorizationTypes.Permanent, scopes, cancellationToken);
            identity.SetAuthorizationId(await authorizations.GetIdAsync(permanent, cancellationToken));
        }

        identity.SetDestinations(GetDestinations);
        return Results.SignIn(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { RedirectUri = MiddtIdentityDefaults.DevicePath + "?status=approved" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Local URL of the current authorize request (form posts become a query string) without <c>prompt</c> and the denial marker.</summary>
    private static string AuthorizeUrl(HttpContext context)
    {
        var source = HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType
            ? context.Request.Form.Select(p => p)
            : context.Request.Query.Select(p => p);
        var parameters = source.Where(p => p.Key is not (Parameters.Prompt or ConsentDeniedParameter)).ToList();
        return context.Request.PathBase + context.Request.Path + QueryString.Create(parameters);
    }

    internal static async Task<ClaimsIdentity> CreateIdentityAsync(IServiceProvider services, MiddtIdentityUser user, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id);
        await SetUserClaimsAsync(services, identity, user);
        identity.SetScopes(scopes);
        var resources = new List<string>();
        await foreach (var resource in services.GetRequiredService<IOpenIddictScopeManager>().ListResourcesAsync(scopes, cancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetResources(resources);
        return identity;
    }

    private static async Task SetUserClaimsAsync(IServiceProvider services, ClaimsIdentity identity, MiddtIdentityUser user)
    {
        var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
        identity.SetClaim(Claims.Name, Journeys.JourneyTask.NameOf(user))
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);
    }

    /// <summary>Access token: sub, name, preferred_username, email, role. Identity token: sub + the claims of the granted scopes.</summary>
    internal static IEnumerable<string> GetDestinations(Claim claim)
    {
        var principal = claim.Subject;
        switch (claim.Type)
        {
            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (principal?.HasScope(Scopes.Profile) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (principal?.HasScope(Scopes.Email) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (principal?.HasScope(Scopes.Roles) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.AuthenticationTime:
                yield return Destinations.IdentityToken;
                yield break;

            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }

    // ───────────────────────────── token ─────────────────────────────

    private static async Task<IResult> TokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType() || request.IsDeviceCodeGrantType())
        {
            // The code/refresh/device token was validated by OpenIddict; the user must still exist and be allowed to sign in.
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
            var options = services.GetRequiredService<IOptions<MiddtIdentityOptions>>().Value;
            var user = result.Principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
            if (user is null || await users.IsLockedOutAsync(user) || (options.RequireConfirmedEmail && !user.EmailConfirmed))
            {
                return Forbid(Errors.InvalidGrant, "Belirteç artık geçerli değil.");
            }

            var identity = new ClaimsIdentity(result.Principal!.Claims, TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            await SetUserClaimsAsync(services, identity, user);
            identity.SetDestinations(GetDestinations);
            return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsPasswordGrantType())
        {
            var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
            var options = services.GetRequiredService<IOptions<MiddtIdentityOptions>>().Value;
            var userName = request.Username ?? string.Empty;
            var user = await users.FindByNameAsync(userName) ?? await users.FindByEmailAsync(userName);
            if (user is null || await users.IsLockedOutAsync(user) || (options.RequireConfirmedEmail && !user.EmailConfirmed))
            {
                return Forbid(Errors.InvalidGrant, "Kullanıcı adı veya şifre hatalı.");
            }

            if (await users.GetTwoFactorEnabledAsync(user))
            {
                return Forbid(Errors.InvalidGrant, "İki adımlı doğrulama etkin hesaplarda şifre grant türü kullanılamaz.");
            }

            if (!await users.CheckPasswordAsync(user, request.Password ?? string.Empty))
            {
                if (users.SupportsUserLockout)
                {
                    await users.AccessFailedAsync(user);
                }

                return Forbid(Errors.InvalidGrant, "Kullanıcı adı veya şifre hatalı.");
            }

            if (users.SupportsUserLockout)
            {
                await users.ResetAccessFailedCountAsync(user);
            }

            user.LastLoginAt = services.GetRequiredService<TimeProvider>().GetUtcNow();
            await users.UpdateAsync(user);

            var identity = await CreateIdentityAsync(services, user, request.GetScopes(), cancellationToken);
            identity.SetDestinations(GetDestinations);
            return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsTokenExchangeGrantType())
        {
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal?.GetClaim(Claims.Subject);
            if (string.IsNullOrEmpty(subject))
            {
                return Forbid(Errors.InvalidGrant, "Belirteç artık geçerli değil.");
            }

            var requestedScopes = request.GetScopes();
            var scopes = requestedScopes.IsDefaultOrEmpty ? result.Principal!.GetScopes() : requestedScopes;
            var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
            var options = services.GetRequiredService<IOptions<MiddtIdentityOptions>>().Value;
            var user = await users.FindByIdAsync(subject);
            if (user is not null)
            {
                if (await users.IsLockedOutAsync(user) || (options.RequireConfirmedEmail && !user.EmailConfirmed))
                {
                    return Forbid(Errors.InvalidGrant, "Belirteç artık geçerli değil.");
                }

                var identity = await CreateIdentityAsync(services, user, scopes, cancellationToken);
                if (!string.IsNullOrEmpty(request.ClientId))
                {
                    identity.SetClaim("act", new System.Text.Json.Nodes.JsonObject { [Claims.Subject] = request.ClientId });
                }

                identity.SetDestinations(GetDestinations);
                return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(subject, cancellationToken) is { } subjectApplication)
            {
                return await SignInClientAsync(services, applications, subjectApplication, subject, scopes, cancellationToken);
            }

            return Forbid(Errors.InvalidGrant, "Belirteç artık geçerli değil.");
        }

        if (request.IsClientCredentialsGrantType())
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
                ?? throw new InvalidOperationException("The client application cannot be found.");

            return await SignInClientAsync(services, applications, application, request.ClientId!, request.GetScopes(), cancellationToken);
        }

        return Forbid(Errors.UnsupportedGrantType, "Desteklenmeyen grant türü.");
    }

    private static async Task<IResult> SignInClientAsync(
        IServiceProvider services,
        IOpenIddictApplicationManager applications,
        object application,
        string clientId,
        ImmutableArray<string> scopes,
        CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, clientId)
            .SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId);
        identity.SetScopes(scopes);
        var resources = new List<string>();
        await foreach (var resource in services.GetRequiredService<IOpenIddictScopeManager>().ListResourcesAsync(scopes, cancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetResources(resources);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // ───────────────────────────── userinfo ─────────────────────────────

    private static async Task<IResult> UserInfoAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var users = context.RequestServices.GetRequiredService<UserManager<MiddtIdentityUser>>();
        var principal = result.Principal;
        var user = principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
        if (principal is null || user is null)
        {
            return Results.Challenge(ErrorProperties(Errors.InvalidToken, "Belirteç artık geçerli değil."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal) { [Claims.Subject] = user.Id };
        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = Journeys.JourneyTask.NameOf(user);
            claims[Claims.PreferredUsername] = user.UserName;
        }

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await users.GetRolesAsync(user);
        }

        return Results.Ok(claims);
    }

    // ───────────────────────────── end session ─────────────────────────────

    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        // RP-initiated logout: end the IdP session; OpenIddict redirects to the validated post_logout_redirect_uri (else "/").
        await context.SignOutAsync(MiddtIdentityDefaults.CookieScheme);
        AuthJourneyCookie.Delete(context);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    // ───────────────────────────── journey completion ─────────────────────────────

    private static async Task<IResult> CompleteJourneyAsync(Guid id, HttpContext context, CancellationToken cancellationToken)
    {
        if (AuthJourneyCookie.Read(context, id) is not { } binding)
        {
            return Results.Redirect(MiddtIdentityDefaults.LoginPath);
        }

        var services = context.RequestServices;
        var completion = await services.GetRequiredService<IAuthJourneyService>().CompleteAsync(id, binding, cancellationToken);
        if (completion is null)
        {
            return Results.Redirect(MiddtIdentityDefaults.JourneyPath(id));
        }

        if (completion is { SignIn: true, UserId: { } userId })
        {
            var users = services.GetRequiredService<UserManager<MiddtIdentityUser>>();
            var user = await users.FindByIdAsync(userId);
            if (user is null || await users.IsLockedOutAsync(user))
            {
                AuthJourneyCookie.Delete(context);
                return Results.Redirect(MiddtIdentityDefaults.LoginPath);
            }

            if (completion.Journey is AuthJourneys.Login or AuthJourneys.Register)
            {
                user.LastLoginAt = services.GetRequiredService<TimeProvider>().GetUtcNow();
                await users.UpdateAsync(user);
            }

            await MiddtIdentitySession.SignInAsync(context, user);
        }

        AuthJourneyCookie.Delete(context);
        return Results.LocalRedirect(AuthJourneyService.IsLocalUrl(completion.ReturnUrl) ? completion.ReturnUrl! : "/");
    }

    private static AuthenticationProperties ErrorProperties(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}

/// <summary>The identity provider's own session (cookie <see cref="MiddtIdentityDefaults.CookieName"/>).</summary>
public static class MiddtIdentitySession
{
    private const string SecurityStampClaim = "AspNet.Identity.SecurityStamp";
    private const string AuthenticationTimeClaim = "auth_time";

    /// <summary>When the user authenticated (sign-in of the session; not changed by sliding renewal) – used for <c>max_age</c>.</summary>
    public static DateTimeOffset? GetAuthenticationTime(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirst(AuthenticationTimeClaim)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>
    /// Signs <paramref name="user"/> into the IdP cookie (claims: name identifier, name, email, roles, security stamp, auth_time). Call it
    /// again after changes that update the security stamp (password change, MFA) to keep the current session.
    /// </summary>
    public static async Task SignInAsync(HttpContext context, MiddtIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);
        var users = context.RequestServices.GetRequiredService<UserManager<MiddtIdentityUser>>();
        var identity = new ClaimsIdentity(MiddtIdentityDefaults.CookieScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, Journeys.JourneyTask.NameOf(user)));
        if (!string.IsNullOrEmpty(user.Email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in await users.GetRolesAsync(user))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        identity.AddClaim(new Claim(SecurityStampClaim, await users.GetSecurityStampAsync(user) ?? string.Empty));
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        identity.AddClaim(new Claim(AuthenticationTimeClaim, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        await context.SignInAsync(MiddtIdentityDefaults.CookieScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
    }

    /// <summary>
    /// The user of an IdP session when it still exists, is not locked out and its security stamp is unchanged (a password
    /// reset or MFA change ends other sessions at their next authorization request).
    /// </summary>
    public static async Task<MiddtIdentityUser?> GetValidUserAsync(UserManager<MiddtIdentityUser> users, ClaimsPrincipal? principal)
    {
        ArgumentNullException.ThrowIfNull(users);
        if (principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value is not { } id || await users.FindByIdAsync(id) is not { } user)
        {
            return null;
        }

        var stamp = principal.FindFirst(SecurityStampClaim)?.Value;
        return await users.IsLockedOutAsync(user) || !string.Equals(stamp, await users.GetSecurityStampAsync(user), StringComparison.Ordinal) ? null : user;
    }
}
