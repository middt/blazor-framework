using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Middt.Framework.Api;
using Middt.Framework.Api.DependencyInjection;

namespace Middt.Framework.Tests.Api;

/// <summary>Echoes the claims the framework reads (<see cref="ClaimTypes"/>) of the authenticated caller.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize]
public sealed class CallerController : BaseController
{
    [HttpGet]
    public IActionResult Get() => Ok(new CallerInfo(
        User.Identity?.Name,
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        User.FindFirst(ClaimTypes.Sid)?.Value,
        User.FindFirst(ClaimTypes.Email)?.Value,
        [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value)],
        User.IsInRole("manager")));
}

public sealed record CallerInfo(string? Name, string? NameIdentifier, string? Sid, string? Email, List<string> Roles, bool IsManager);

/// <summary>Fake OIDC provider + an API host in authority mode (no JwtSettings), discovery through the backchannel authority.</summary>
public sealed class AuthorityApiFixture : IAsyncLifetime
{
    public FakeOidcProvider Provider { get; private set; } = null!;

    public TestApiHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Provider = await FakeOidcProvider.StartAsync();
        Host = await TestApiHost.StartAsync(
            environment: "Production",
            settings: new Dictionary<string, string?>
            {
                // JwtSettings are not required in authority mode: an invalid (empty) secret must not stop the host.
                ["JwtSettings:Secret"] = string.Empty,
                ["Authentication:Authority"] = FakeOidcProvider.Issuer,
                ["Authentication:BackchannelAuthority"] = FakeOidcProvider.BackchannelAuthority,
                ["Authentication:RequireHttpsMetadata"] = "false",
            },
            configureBuilder: builder => builder.Services
                .AddHttpClient(MiddtApiExtensions.AuthorityBackchannelClientName)
                .ConfigurePrimaryHttpMessageHandler(Provider.CreateHandler));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Provider.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class AuthorityAuthenticationTests(AuthorityApiFixture fixture) : IClassFixture<AuthorityApiFixture>
{
    private const string SecureUri = "api/v1/secureitems/getall";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpStatusCode> GetSecureAsync(string? token)
    {
        using var request = TestApiHost.Authorized(HttpMethod.Get, SecureUri, token);
        using var response = await fixture.Host.Client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    [Fact]
    public async Task Access_token_of_the_authority_is_accepted()
    {
        (await GetSecureAsync(fixture.Provider.CreateAccessToken())).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_token_is_rejected()
    {
        (await GetSecureAsync(null)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        (await GetSecureAsync(fixture.Provider.CreateAccessToken(audience: "other-api"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_of_another_issuer_is_rejected()
    {
        (await GetSecureAsync(fixture.Provider.CreateAccessToken(issuer: "http://evil.test/"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = fixture.Provider.CreateAccessToken(expires: DateTime.UtcNow.AddMinutes(-10));

        (await GetSecureAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_signed_with_an_unknown_key_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var token = fixture.Provider.CreateAccessToken(signingKey: new RsaSecurityKey(rsa) { KeyId = fixture.Provider.Key.KeyId });

        (await GetSecureAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Symmetric_token_of_the_token_controller_is_rejected_in_authority_mode()
    {
        var symmetric = new Middt.Framework.Api.Security.JwtTokenGenerator(
            Microsoft.Extensions.Options.Options.Create(new Middt.Framework.Api.Configuration.Model.JwtSettings
            {
                Secret = TestApiHost.Secret,
                Issuer = FakeOidcProvider.Issuer,
                Audience = FakeOidcProvider.Audience,
            }),
            TimeProvider.System).CreateToken([new Claim("sub", "x")]);

        (await GetSecureAsync(symmetric.access_token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Oidc_claims_are_mapped_to_the_framework_claim_types()
    {
        using var request = TestApiHost.Authorized(HttpMethod.Get, "api/v1/caller", fixture.Provider.CreateAccessToken());
        using var response = await fixture.Host.Client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var caller = (await response.Content.ReadFromJsonAsync<CallerInfo>(TestApiHost.Json, Ct))!;
        caller.Name.Should().Be(FakeOidcProvider.UserName);
        caller.NameIdentifier.Should().Be(FakeOidcProvider.Subject);
        caller.Sid.Should().Be(FakeOidcProvider.Subject);
        caller.Email.Should().Be(FakeOidcProvider.Email);
        caller.Roles.Should().BeEquivalentTo(FakeOidcProvider.Roles);
        caller.IsManager.Should().BeTrue("role claims drive IsInRole / [Authorize(Roles)]");
    }

    [Fact]
    public async Task Query_string_token_is_ignored_outside_the_hub_path()
    {
        var token = fixture.Provider.CreateAccessToken();

        using var response = await fixture.Host.Client.GetAsync($"api/v1/caller?access_token={token}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "query tokens are only accepted under HubPathPrefix");
    }

    [Fact]
    public async Task Discovery_and_keys_are_fetched_through_the_backchannel_authority()
    {
        (await GetSecureAsync(fixture.Provider.CreateAccessToken())).Should().Be(HttpStatusCode.OK);

        fixture.Provider.Hosts.Should().NotBeEmpty().And.OnlyContain(h => h == "idp.internal");
    }
}

public sealed class AuthorityModeOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Options_override_configuration_and_hub_query_tokens_work()
    {
        await using var provider = await FakeOidcProvider.StartAsync();
        await using var host = await TestApiHost.StartAsync(
            environment: "Development",
            configure: o =>
            {
                o.Authority = FakeOidcProvider.Issuer;
                o.Audience = "custom-api";
                o.HubPathPrefix = "/api/v1/caller";
            },
            configureBuilder: builder => builder.Services
                .AddHttpClient(MiddtApiExtensions.AuthorityBackchannelClientName)
                .ConfigurePrimaryHttpMessageHandler(provider.CreateHandler));

        using var wrongAudience = TestApiHost.Authorized(HttpMethod.Get, "api/v1/caller", provider.CreateAccessToken());
        (await host.Client.SendAsync(wrongAudience, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // HubPathPrefix points at the caller endpoint here: the token may come from the access_token query parameter.
        using var response = await host.Client.GetAsync($"api/v1/caller?access_token={provider.CreateAccessToken(audience: "custom-api")}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.Hosts.Should().Contain("idp.test", "without a backchannel authority the public authority is called");
    }

    [Fact]
    public async Task Without_authority_JwtSettings_are_still_validated_at_startup()
    {
        var start = () => TestApiHost.StartAsync(settings: new Dictionary<string, string?> { ["JwtSettings:Secret"] = string.Empty });

        await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
    }
}
