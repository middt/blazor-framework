using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Middt.Framework.Client.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Middt.Framework.Blazor.Web.Security;

namespace Middt.Framework.Tests.Security;

public sealed class JwtClaimsReaderTests
{
    private static string CreateToken(DateTime expires, params Claim[] claims)
    {
        var notBefore = expires.AddHours(-1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("claims-reader-test-key-0123456789abcdef")), SecurityAlgorithms.HmacSha256),
        });
    }

    private static readonly Claim[] Claims =
    [
        new(ClaimTypes.Sid, "42"),
        new(ClaimTypes.Name, "bob"),
        new(ClaimTypes.Role, "admin"),
        new(ClaimTypes.Role, "editor"),
    ];

    [Fact]
    public void Valid_token_produces_authenticated_identity_with_name_and_roles()
    {
        var identity = JwtClaimsReader.ReadIdentity(CreateToken(DateTime.UtcNow.AddMinutes(10), Claims));

        identity.IsAuthenticated.Should().BeTrue();
        identity.Name.Should().Be("bob");
        identity.FindFirst(ClaimTypes.Sid)!.Value.Should().Be("42");
        identity.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().BeEquivalentTo(["admin", "editor"]);
        new ClaimsPrincipal(identity).IsInRole("editor").Should().BeTrue();
    }

    [Fact]
    public void Expired_token_produces_unauthenticated_identity()
    {
        JwtClaimsReader.ReadIdentity(CreateToken(DateTime.UtcNow.AddMinutes(-5), Claims)).IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.not-base64!.sig")]
    public void Missing_or_malformed_token_produces_unauthenticated_identity(string? token)
    {
        JwtClaimsReader.ReadIdentity(token).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task BaseTokenHelper_exposes_claims_from_the_authentication_state()
    {
        var identity = JwtClaimsReader.ReadIdentity(CreateToken(DateTime.UtcNow.AddMinutes(10), Claims));
        var helper = new BaseTokenHelper(new FixedAuthenticationStateProvider(new ClaimsPrincipal(identity)));

        await helper.LoadAsync();

        helper.IsAuthenticated.Should().BeTrue();
        helper.UserID().Should().Be(42);
        helper.Username().Should().Be("bob");
        helper.RoleList().Should().BeEquivalentTo(["admin", "editor"]);
        helper.IsHasRoles(["guest", "editor"]).Should().BeTrue();
        helper.IsHasRole("guest").Should().BeFalse();
    }

    [Fact]
    public async Task BaseTokenHelper_without_token_is_anonymous()
    {
        var helper = new BaseTokenHelper(new FixedAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        await helper.LoadAsync();

        helper.IsAuthenticated.Should().BeFalse();
        helper.UserID().Should().Be(-1);
        helper.Username().Should().BeEmpty();
    }
}

internal sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
}
