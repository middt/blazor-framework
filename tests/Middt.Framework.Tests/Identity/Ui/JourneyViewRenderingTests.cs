using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Middt.Identity;
using Middt.Identity.Ui;
using Middt.Identity.Ui.Components.Account.Views;
using Middt.Identity.Ui.Components.Shared;

namespace Middt.Framework.Tests.Identity.Ui;

/// <summary>Journey views rendered with <see cref="HtmlRenderer"/> (static markup, as in static server rendering).</summary>
public sealed partial class JourneyViewRenderingTests
{
    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddMiddtIdentityUi()
            .AddSingleton<AntiforgeryStateProvider, TestAntiforgery>()
            .AddSingleton<AuthenticationStateProvider, AnonymousAuthentication>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private static Task<string> RenderViewAsync<TView>(AuthJourneyState state, IReadOnlyList<string>? errors = null, IReadOnlyDictionary<string, string>? values = null)
        where TView : IComponent =>
        RenderAsync<TView>(new Dictionary<string, object?>
        {
            ["State"] = state,
            ["Errors"] = errors ?? [],
            ["Values"] = values ?? new Dictionary<string, string>(),
        });

    private static AuthJourneyState State(string view, params AuthJourneyAction[] actions) => new()
    {
        Id = Guid.NewGuid(),
        Journey = AuthJourneys.Login,
        State = view,
        View = view,
        Actions = actions,
    };

    private static AuthJourneyAction Action(string key, string? schema = null) =>
        new() { Key = key, InputSchema = schema is null ? null : JsonNode.Parse(schema)!.AsObject() };

    [Fact]
    public async Task Credentials_view_keeps_the_e2e_ids_and_autocomplete_hints()
    {
        var html = await RenderViewAsync<CredentialsView>(State("credentials", Action("submit-credentials")),
            values: new Dictionary<string, string> { ["userName"] = "ali" });

        html.Should().Contain("id=\"Input_Username\"").And.Contain("name=\"userName\"").And.Contain("autocomplete=\"username\"");
        html.Should().Contain("id=\"Input_Password\"").And.Contain("name=\"password\"").And.Contain("autocomplete=\"current-password\"");
        html.Should().Contain("value=\"ali\"");
        html.Should().Contain("<label for=\"Input_Username\"").And.Contain("<label for=\"Input_Password\"");
        html.Should().Contain("name=\"__action\" value=\"submit-credentials\"");
        html.Should().Contain("name=\"__RequestVerificationToken\" value=\"test-token\"");
        html.Should().Contain("method=\"post\"").And.NotContain("data-enhance");
        SubmitButtons().Count(html).Should().Be(1);
        Autofocus().Matches(html).Single().Value.Should().Contain("Input_Username");
    }

    [Fact]
    public async Task Errors_are_announced_in_an_alert()
    {
        var html = await RenderViewAsync<CredentialsView>(State("credentials", Action("submit-credentials")), ["Kullanıcı adı veya şifre hatalı."]);

        html.Should().Contain("role=\"alert\"").And.Contain("Kullanıcı adı veya şifre hatalı.");
    }

    [Fact]
    public async Task Totp_and_email_code_inputs_are_one_time_codes()
    {
        var html = await RenderViewAsync<TotpView>(State("totp", Action("submit-totp")));

        html.Should().Contain("autocomplete=\"one-time-code\"").And.Contain("inputmode=\"numeric\"").And.Contain("name=\"code\"");
    }

    [Fact]
    public async Task Consent_view_posts_approve_or_deny()
    {
        var state = new AuthJourneyState
        {
            Journey = AuthJourneys.Login,
            View = "consent",
            Data = new JsonObject { ["clientName"] = "Middt Blazor", ["scopes"] = new JsonArray("openid", "email", "api") },
            Actions = [Action("approve"), Action("deny")],
        };

        var html = await RenderViewAsync<ConsentView>(state);

        html.Should().Contain("Middt Blazor").And.Contain("E-posta adresiniz").And.Contain("<code class=\"idp-muted\">api</code>");
        html.Should().Contain("value=\"approve\"").And.Contain("value=\"deny\"");
    }

    [Fact]
    public async Task Totp_setup_renders_the_qr_code_inline()
    {
        var state = new AuthJourneyState
        {
            Journey = AuthJourneys.EnrollTotp,
            View = "totp-setup",
            Data = new JsonObject { ["totpUri"] = "otpauth://totp/Middt:ali?secret=JBSWY3DPEHPK3PXP&issuer=Middt", ["totpKey"] = "JBSWY3DPEHPK3PXP" },
            Actions = [Action("confirm")],
        };

        var html = await RenderViewAsync<TotpSetupView>(state);

        html.Should().Contain("<svg").And.Contain("jbsw y3dp ehpk 3pxp").And.Contain("autocomplete=\"one-time-code\"");
    }

    [Fact]
    public async Task Generic_view_renders_a_form_from_the_action_schema_without_echoing_secrets()
    {
        var state = State("terms",
            Action("accept-terms", """
                {"type":"object","properties":{
                  "nickname":{"type":"string","title":"Takma ad"},
                  "secretAnswer":{"type":"string","x-sensitive":true},
                  "agree":{"type":"boolean","title":"Kabul ediyorum"},
                  "plan":{"type":"string","enum":["basic","pro"]}},
                 "required":["nickname"]}
                """),
            Action("cancel"));

        var html = await RenderViewAsync<GenericJourneyView>(state,
            values: new Dictionary<string, string> { ["nickname"] = "ali", ["secretAnswer"] = "leak" });

        html.Should().Contain("<label for=\"Input_accept_terms_nickname\">Takma ad</label>").And.Contain("value=\"ali\"");
        html.Should().Contain("type=\"password\"").And.NotContain("leak");
        html.Should().Contain("type=\"checkbox\"").And.Contain("<option value=\"pro\"");
        html.Should().Contain("value=\"accept-terms\"").And.Contain("value=\"cancel\"");
        Autofocus().Matches(html).Should().ContainSingle().Which.Value.Should().Contain("Input_accept_terms_nickname");
    }

    [Fact]
    public async Task Error_summary_lists_several_errors()
    {
        var html = await RenderAsync<ErrorSummary>(new Dictionary<string, object?> { ["Errors"] = new[] { "A", "B" } });

        html.Should().Contain("role=\"alert\"").And.Contain("<li>A</li>").And.Contain("<li>B</li>");
        (await RenderAsync<ErrorSummary>(new Dictionary<string, object?> { ["Errors"] = Array.Empty<string>() })).Should().BeEmpty();
    }

    [GeneratedRegex("<button[^>]*type=\"submit\"")]
    private static partial Regex SubmitButtons();

    [GeneratedRegex("<input[^>]*autofocus[^>]*>")]
    private static partial Regex Autofocus();

    private sealed class AnonymousAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }

    private sealed class TestAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
