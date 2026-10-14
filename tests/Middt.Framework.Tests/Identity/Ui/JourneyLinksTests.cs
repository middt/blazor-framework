using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Middt.Identity;
using Middt.Identity.Ui;
using Middt.Identity.Ui.Components.Account.Views;
using Middt.Identity.Ui.Journeys;

namespace Middt.Framework.Tests.Identity.Ui;

public sealed class JourneyLinksTests
{
    [Theory]
    [InlineData("/connect/authorize?x=1", "/connect/authorize?x=1")]
    [InlineData("~/admin", "/admin")]
    [InlineData("https://evil.example", null)]
    [InlineData("//evil.example", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData(null, null)]
    public void Only_local_return_urls_are_kept(string? returnUrl, string? expected) =>
        JourneyLinks.SafeReturnUrl(returnUrl).Should().Be(expected);

    [Fact]
    public void An_authorization_request_passes_client_and_scopes_to_the_journey()
    {
        var returnUrl = "/connect/authorize?client_id=middt-blazor&scope=openid%20profile%20api&response_type=code";

        var start = JourneyLinks.CreateStart(returnUrl);

        start.ReturnUrl.Should().Be(returnUrl);
        start.ClientId.Should().Be("middt-blazor");
        start.Scopes.Should().Equal("openid", "profile", "api");
    }

    [Fact]
    public void Other_return_urls_only_set_the_return_url()
    {
        JourneyLinks.CreateStart("/account/security").Should().BeEquivalentTo(new AuthJourneyStart { ReturnUrl = "/account/security" });
        JourneyLinks.CreateStart("https://evil.example/connect/authorize?client_id=x").ReturnUrl.Should().BeNull();
    }

    [Fact]
    public void Failed_journeys_restart_at_their_page()
    {
        JourneyLinks.StartPath(AuthJourneys.Login, "/connect/authorize?a=1").Should().Be("/account/login?returnUrl=%2Fconnect%2Fauthorize%3Fa%3D1");
        JourneyLinks.StartPath(AuthJourneys.Register, null).Should().Be(MiddtIdentityDefaults.RegisterPath);
        JourneyLinks.StartPath(AuthJourneys.ResetPassword, "https://evil.example").Should().Be(MiddtIdentityDefaults.ForgotPasswordPath);
        JourneyLinks.StartPath(AuthJourneys.EnrollTotp, "/x").Should().Be(MiddtIdentityUi.SecurityPath);
        JourneyLinks.StartPath("custom", null).Should().Be(MiddtIdentityDefaults.LoginPath);
    }

    [Fact]
    public void Journey_views_are_registered_by_name_and_can_be_replaced()
    {
        using var services = new ServiceCollection()
            .AddMiddtIdentityUi(o => o.AddJourneyView<GenericJourneyView>(JourneyViewNames.Consent).AddJourneyView<TotpView>("terms"))
            .BuildServiceProvider();
        var registry = services.GetRequiredService<JourneyViewRegistry>();

        registry.Resolve("credentials").Should().Be<CredentialsView>();
        registry.Resolve("CREDENTIALS").Should().Be<CredentialsView>();
        registry.Resolve("totp-setup").Should().Be<TotpSetupView>();
        registry.Resolve("consent").Should().Be<GenericJourneyView>();
        registry.Resolve("terms").Should().Be<TotpView>();
        registry.Resolve("unknown").Should().Be<GenericJourneyView>();
        registry.Resolve(null).Should().Be<GenericJourneyView>();
    }

    [Fact]
    public void Only_components_can_be_registered_as_views()
    {
        var options = new MiddtIdentityUiOptions();

        options.Invoking(o => o.AddJourneyView("x", typeof(string))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Journeys_that_end_with_a_local_redirect_continue_there()
    {
        AuthJourneyState Finished(string? redirect, bool succeeded = false) => new()
        {
            Journey = AuthJourneys.Login,
            IsFinished = true,
            IsSucceeded = succeeded,
            ReturnUrl = "/connect/authorize?client_id=x",
            Data = redirect is null ? [] : new JsonObject { ["redirect"] = redirect },
        };

        JourneyLinks.FinishedRedirect(Finished("/account/register")).Should().Be("/account/register?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dx");
        JourneyLinks.FinishedRedirect(Finished("https://evil.example")).Should().BeNull();
        JourneyLinks.FinishedRedirect(Finished(null)).Should().BeNull();
        JourneyLinks.FinishedRedirect(Finished("/account/register", succeeded: true)).Should().BeNull();
    }

    [Fact]
    public void Qr_codes_are_only_made_for_otpauth_uris()
    {
        QrCodeSvg.Create("otpauth://totp/Middt:ali?secret=JBSWY3DPEHPK3PXP&issuer=Middt").Should().StartWith("<svg").And.Contain("viewBox");
        QrCodeSvg.Create("https://evil.example").Should().BeNull();
        QrCodeSvg.Create(null).Should().BeNull();
        QrCodeSvg.FormatKey("JBSWY3DPEHPK3PXP").Should().Be("jbsw y3dp ehpk 3pxp");
    }
}
