using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Middt.Framework.Tests.Api;

/// <summary>
/// Minimal in-process OpenID Connect provider on a TestServer: discovery, JWKS (RSA), authorization endpoint that
/// immediately redirects back with a code for the test user (PKCE S256 checked at the token endpoint), token endpoint
/// (authorization_code, refresh_token with rotation), userinfo, revocation and end session. Records what it receives.
/// Its issuer is the public URL <see cref="Issuer"/>; it answers on any host, so relying parties can reach it through a
/// backchannel authority (<see cref="Hosts"/> shows which host was used).
/// </summary>
public sealed class FakeOidcProvider : IAsyncDisposable
{
    public const string Issuer = "http://idp.test/";
    public const string BackchannelAuthority = "http://idp.internal/";
    public const string ClientId = "test-client";
    public const string ClientSecret = "test-secret";
    public const string Audience = "middt-api";
    public const string Subject = "5f9c2b7e-0d7a-4c4e-9a43-2f3c3f6f1a11";
    public const string UserName = "Alice Example";
    public const string Email = "alice@example.com";
    public static readonly string[] Roles = ["manager", "ui-admin"];

    private readonly ConcurrentDictionary<string, CodeGrant> codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> refreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> accessTokens = new(StringComparer.Ordinal);

    private FakeOidcProvider(WebApplication app, RsaSecurityKey key, TimeProvider time)
    {
        App = app;
        Key = key;
        Time = time;
    }

    public WebApplication App { get; }

    public RsaSecurityKey Key { get; }

    public TimeProvider Time { get; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Host header of every request (e.g. <c>idp.internal</c> when called through the backchannel authority).</summary>
    public ConcurrentQueue<string> Hosts { get; } = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> AuthorizeRequests { get; } = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> TokenRequests { get; } = new();

    public ConcurrentQueue<string> RevokedTokens { get; } = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> EndSessionRequests { get; } = new();

    public int UserInfoCalls => userInfoCalls;

    private int userInfoCalls;

    public static async Task<FakeOidcProvider> StartAsync(TimeProvider? time = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();

        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fake-oidc-key" };
        var provider = new FakeOidcProvider(app, key, time ?? TimeProvider.System);
        provider.Map(app);
        await app.StartAsync();
        return provider;
    }

    /// <summary>Handler that sends requests into the provider (use as primary handler of a backchannel client).</summary>
    public HttpMessageHandler CreateHandler() => App.GetTestServer().CreateHandler();

    /// <summary>Client for browser-side requests (authorize, logout); does not follow redirects.</summary>
    public HttpClient CreateClient() => App.GetTestClient();

    /// <summary>Signs an access token like the provider does; parameters allow invalid variants.</summary>
    public string CreateAccessToken(
        string? audience = Audience,
        string issuer = Issuer,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        IEnumerable<string>? roles = null,
        string? name = UserName)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        var exp = expires ?? now.Add(AccessTokenLifetime);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Subject,
            ["email"] = Email,
            ["role"] = (roles ?? Roles).ToArray(),
            ["scope"] = "openid profile email roles offline_access api",
            ["client_id"] = ClientId,
        };
        if (name is not null)
        {
            claims["name"] = name;
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = exp <= now ? exp.AddMinutes(-5) : now,
            NotBefore = exp <= now ? exp.AddMinutes(-5) : now,
            Expires = exp,
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(signingKey ?? Key, SecurityAlgorithms.RsaSha256),
        });
        accessTokens[token] = true;
        return token;
    }

    private string CreateIdToken(string? nonce)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object> { ["sub"] = Subject, ["name"] = UserName, ["email"] = Email };
        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }

    private void Map(WebApplication app)
    {
        app.Use((context, next) =>
        {
            Hosts.Enqueue(context.Request.Host.Host);
            return next(context);
        });

        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = Issuer,
            ["authorization_endpoint"] = Issuer + "connect/authorize",
            ["token_endpoint"] = Issuer + "connect/token",
            ["userinfo_endpoint"] = Issuer + "connect/userinfo",
            ["end_session_endpoint"] = Issuer + "connect/logout",
            ["revocation_endpoint"] = Issuer + "connect/revoke",
            ["jwks_uri"] = Issuer + ".well-known/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query", "form_post" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["scopes_supported"] = new[] { "openid", "profile", "email", "roles", "offline_access", "api" },
        }));

        app.MapGet("/.well-known/jwks", () =>
        {
            var parameters = Key.Rsa.ExportParameters(false);
            return Results.Json(new
            {
                keys = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["kty"] = "RSA",
                        ["use"] = "sig",
                        ["alg"] = "RS256",
                        ["kid"] = Key.KeyId,
                        ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                        ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
                    },
                },
            });
        });

        app.MapGet("/connect/authorize", (HttpContext context) =>
        {
            var query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
            AuthorizeRequests.Enqueue(query);
            if (query.GetValueOrDefault("client_id") != ClientId || query.GetValueOrDefault("response_type") != "code"
                || query.GetValueOrDefault("code_challenge_method") != "S256" || !query.TryGetValue("redirect_uri", out var redirectUri))
            {
                return Results.BadRequest();
            }

            var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));
            codes[code] = new CodeGrant(query.GetValueOrDefault("nonce"), query.GetValueOrDefault("code_challenge") ?? string.Empty, redirectUri);
            return Results.Redirect($"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(query.GetValueOrDefault("state") ?? string.Empty)}");
        });

        app.MapPost("/connect/token", async (HttpContext context) =>
        {
            var form = (await context.Request.ReadFormAsync()).ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);
            TokenRequests.Enqueue(form);
            if (!ClientAuthenticated(context.Request, form))
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            string? nonce = null;
            switch (form.GetValueOrDefault("grant_type"))
            {
                case "authorization_code":
                    if (!codes.TryRemove(form.GetValueOrDefault("code") ?? string.Empty, out var grant)
                        || grant.RedirectUri != form.GetValueOrDefault("redirect_uri")
                        || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier") ?? string.Empty))) != grant.CodeChallenge)
                    {
                        return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
                    }

                    nonce = grant.Nonce;
                    break;
                case "refresh_token":
                    // Rotation: every refresh token is single use.
                    if (!refreshTokens.TryRemove(form.GetValueOrDefault("refresh_token") ?? string.Empty, out _))
                    {
                        return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
                    }

                    break;
                default:
                    return Results.Json(new { error = "unsupported_grant_type" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var refreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            refreshTokens[refreshToken] = true;
            return Results.Json(new Dictionary<string, object>
            {
                ["access_token"] = CreateAccessToken(),
                ["token_type"] = "Bearer",
                ["expires_in"] = (int)AccessTokenLifetime.TotalSeconds,
                ["refresh_token"] = refreshToken,
                ["id_token"] = CreateIdToken(nonce),
                ["scope"] = "openid profile email roles offline_access api",
            });
        });

        app.MapMethods("/connect/userinfo", ["GET", "POST"], (HttpContext context) =>
        {
            Interlocked.Increment(ref userInfoCalls);
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !accessTokens.ContainsKey(header["Bearer ".Length..]))
            {
                return Results.Unauthorized();
            }

            return Results.Json(new Dictionary<string, object> { ["sub"] = Subject, ["name"] = UserName, ["email"] = Email, ["role"] = Roles });
        });

        app.MapPost("/connect/revoke", async (HttpContext context) =>
        {
            var form = (await context.Request.ReadFormAsync()).ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);
            if (!ClientAuthenticated(context.Request, form))
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = form.GetValueOrDefault("token") ?? string.Empty;
            RevokedTokens.Enqueue(token);
            refreshTokens.TryRemove(token, out _);
            return Results.Ok();
        });

        app.MapGet("/connect/logout", (HttpContext context) =>
        {
            var query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
            EndSessionRequests.Enqueue(query);
            return query.TryGetValue("post_logout_redirect_uri", out var redirect)
                ? Results.Redirect($"{redirect}?state={Uri.EscapeDataString(query.GetValueOrDefault("state") ?? string.Empty)}")
                : Results.Ok();
        });
    }

    private static bool ClientAuthenticated(HttpRequest request, Dictionary<string, string> form)
    {
        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header) && header.Scheme == "Basic" && header.Parameter is { } parameter)
        {
            var pair = Encoding.UTF8.GetString(Convert.FromBase64String(parameter)).Split(':', 2);
            return pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == ClientId && Uri.UnescapeDataString(pair[1]) == ClientSecret;
        }

        return form.GetValueOrDefault("client_id") == ClientId && form.GetValueOrDefault("client_secret") == ClientSecret;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Key.Rsa.Dispose();
    }

    private sealed record CodeGrant(string? Nonce, string CodeChallenge, string RedirectUri);
}
