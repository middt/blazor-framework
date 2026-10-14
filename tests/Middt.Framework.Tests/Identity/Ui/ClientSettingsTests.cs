using Middt.Identity.Ui.Components.Admin;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Framework.Tests.Identity.Ui;

public sealed class ClientSettingsTests
{
    [Fact]
    public void Settings_map_to_permissions_requirements_and_uris()
    {
        var settings = new ClientSettings
        {
            ClientId = " middt-blazor ",
            DisplayName = "Middt Blazor",
            ClientType = ClientTypes.Confidential,
            RedirectUris = "http://localhost:8080/signin-oidc\r\nhttp://localhost:8080/signin-oidc\n",
            PostLogoutRedirectUris = "http://localhost:8080/signout-callback-oidc",
            Scopes = ["profile", "api"],
        };
        var descriptor = new OpenIddictApplicationDescriptor();

        settings.ApplyTo(descriptor).Should().BeEmpty();

        descriptor.ClientId.Should().Be("middt-blazor");
        descriptor.RedirectUris.Should().ContainSingle().Which.Should().Be(new Uri("http://localhost:8080/signin-oidc"));
        descriptor.PostLogoutRedirectUris.Should().ContainSingle();
        descriptor.Permissions.Should().Contain([
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + "profile", Permissions.Prefixes.Scope + "api"]);
        descriptor.Requirements.Should().Contain(Requirements.Features.ProofKeyForCodeExchange);
    }

    [Fact]
    public void Unmanaged_permissions_are_kept_and_managed_ones_replaced()
    {
        var descriptor = new OpenIddictApplicationDescriptor { ClientId = "x", ClientType = ClientTypes.Confidential };
        descriptor.Permissions.UnionWith(["custom:feature", Permissions.Prefixes.Scope + "old", Permissions.Endpoints.Introspection]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        var settings = ClientSettings.From(descriptor);
        settings.Scopes = ["email"];
        settings.Endpoints = [Permissions.Endpoints.Token];
        settings.GrantTypes = [Permissions.GrantTypes.ClientCredentials];
        settings.RequirePkce = false;

        settings.ApplyTo(descriptor).Should().BeEmpty();

        descriptor.Permissions.Should().BeEquivalentTo(["custom:feature", Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials, Permissions.Prefixes.Scope + "email"]);
        descriptor.Requirements.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_settings_are_reported()
    {
        var settings = new ClientSettings
        {
            ClientId = "",
            ClientType = ClientTypes.Public,
            RedirectUris = "not a uri\nhttps://app.example/cb#fragment",
            GrantTypes = [Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.ClientCredentials],
        };
        var descriptor = new OpenIddictApplicationDescriptor { ClientSecret = "old" };

        var errors = settings.ApplyTo(descriptor);

        errors.Should().HaveCount(5);
        descriptor.ClientSecret.Should().BeNull("public clients have no secret");
    }

    [Fact]
    public void Generated_secrets_are_random_and_url_safe()
    {
        var a = ClientSettings.GenerateSecret();
        var b = ClientSettings.GenerateSecret();

        a.Should().NotBe(b).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
    }
}
