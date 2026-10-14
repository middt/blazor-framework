using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Middt.Identity;
using Middt.Identity.Ui;

namespace Middt.Framework.Tests.Identity.Ui;

/// <summary>
/// The account pages in a real Blazor Web App pipeline (static SSR: redirects, cookies, antiforgery, form dispatch to the
/// journey views) against a fake <see cref="IAuthJourneyService"/>.
/// </summary>
public sealed partial class AccountPagesTests : IAsyncLifetime
{
    private readonly FakeJourneys journeys = new();
    private WebApplication app = default!;
    private readonly Dictionary<string, string> cookies = new(StringComparer.Ordinal);

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMiddtIdentityUi();
        builder.Services.AddSingleton<DevelopmentMailbox>();
        builder.Services.AddSingleton<IIdentityMessageSender>(sp => sp.GetRequiredService<DevelopmentMailbox>());
        builder.Services.AddSingleton<IAuthJourneyService>(journeys);
        builder.Services.AddAuthentication(MiddtIdentityDefaults.CookieScheme)
            .AddCookie(MiddtIdentityDefaults.CookieScheme, o => o.LoginPath = MiddtIdentityDefaults.LoginPath);
        builder.Services.AddAuthorization();

        app = builder.Build();
        app.UseMiddtIdentitySecurityHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapMiddtIdentityUi();
        app.MapGet("/test/signin", (HttpContext context) => context.SignInAsync(MiddtIdentityDefaults.CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, MiddtIdentityDefaults.AdminRole)],
            MiddtIdentityDefaults.CookieScheme))));
        await app.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Dictionary<string, string>? form = null)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(method, url);
        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var pair = header.Split(';')[0].Split('=', 2);
                cookies[pair[0]] = pair[1];
            }
        }

        return response;
    }

    private async Task<(Guid Id, string Html)> StartLoginAsync(string returnUrl = "/connect/authorize?client_id=middt-blazor&scope=openid%20profile")
    {
        var start = await SendAsync(HttpMethod.Get, "/account/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = start.Headers.Location!.OriginalString;
        var id = Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
        var page = await SendAsync(HttpMethod.Get, location);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        return (id, await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, string> HiddenFields(string html, string formName)
    {
        var form = FormRegex().Matches(html).Select(m => m.Value).Single(f => f.Contains($"value=\"{formName}\"", StringComparison.Ordinal));
        return HiddenRegex().Matches(form).ToDictionary(m => WebUtility.HtmlDecode(m.Groups["name"].Value), m => WebUtility.HtmlDecode(m.Groups["value"].Value));
    }

    [Fact]
    public async Task Login_starts_a_bound_journey_and_renders_the_credentials_view()
    {
        var (id, html) = await StartLoginAsync();

        cookies.Should().ContainKey(MiddtIdentityDefaults.JourneyCookieName).WhoseValue.Should().StartWith(id.ToString("N"));
        var start = journeys.Starts.Should().ContainSingle().Subject;
        start.Journey.Should().Be(AuthJourneys.Login);
        start.Start.ClientId.Should().Be("middt-blazor");
        start.Start.Scopes.Should().Equal("openid", "profile");
        html.Should().Contain("id=\"Input_Username\"").And.Contain("id=\"Input_Password\"").And.Contain("lang=\"tr\"");
        html.Should().Contain("data-enhance-nav=\"false\"").And.Contain("middt-identity.css");
    }

    [Fact]
    public async Task Account_pages_send_security_headers_without_form_action()
    {
        using var response = await SendAsync(HttpMethod.Get, MiddtIdentityDefaults.LogoutPath);

        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("frame-ancestors 'none'").And.Contain("script-src 'self'");
        // Sign-in ends with a redirect to the client's redirect_uri after a form post: form-action would block it.
        csp.Should().NotContain("form-action");
    }

    [Fact]
    public async Task External_return_urls_are_dropped()
    {
        await StartLoginAsync("https://evil.example/");

        journeys.Starts.Single().Start.ReturnUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_journey_of_another_browser_is_not_shown()
    {
        var (id, _) = await StartLoginAsync();
        cookies.Clear();

        var html = await (await SendAsync(HttpMethod.Get, MiddtIdentityDefaults.JourneyPath(id))).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        html.Should().Contain("Oturumun süresi doldu").And.NotContain("Input_Password");
    }

    [Fact]
    public async Task Failed_submit_shows_errors_and_keeps_only_safe_values()
    {
        var (id, html) = await StartLoginAsync();
        var form = HiddenFields(html, "submit-credentials");
        form["userName"] = "ali";
        form["password"] = "wrong-secret";

        var response = await SendAsync(HttpMethod.Post, MiddtIdentityDefaults.JourneyPath(id), form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.Should().Contain("role=\"alert\"").And.Contain("Kullanıcı adı veya şifre hatalı.");
        body.Should().Contain("value=\"ali\"").And.NotContain("wrong-secret");
        journeys.Inputs.Single().ToJsonString().Should().Be("""{"userName":"ali","password":"wrong-secret"}""");
    }

    [Fact]
    public async Task Successful_submit_redirects_to_the_completion_endpoint()
    {
        var (id, html) = await StartLoginAsync();
        var form = HiddenFields(html, "submit-credentials");
        form["userName"] = "ali";
        form["password"] = "test";

        var response = await SendAsync(HttpMethod.Post, MiddtIdentityDefaults.JourneyPath(id), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().EndWith(MiddtIdentityDefaults.JourneyCompletePath(id));
    }

    [Fact]
    public async Task Posts_without_antiforgery_token_are_rejected()
    {
        var (id, _) = await StartLoginAsync();

        var response = await SendAsync(HttpMethod.Post, MiddtIdentityDefaults.JourneyPath(id),
            new Dictionary<string, string> { ["_handler"] = "journey-submit-credentials", ["__action"] = "submit-credentials", ["password"] = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        journeys.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task Administration_requires_a_session()
    {
        var response = await SendAsync(HttpMethod.Get, MiddtIdentityUi.AdminPath + "/users");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain(MiddtIdentityDefaults.LoginPath);
    }

    [Fact]
    public async Task Administration_renders_interactively_for_admins()
    {
        await SendAsync(HttpMethod.Get, "/test/signin");

        var response = await SendAsync(HttpMethod.Get, MiddtIdentityUi.AdminPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        html.Should().Contain("Yönetim menüsü").And.Contain("Kullanıcılar").And.Contain("Radzen.Blazor.js").And.Contain("<!--Blazor:");
    }

    [Fact]
    public async Task Logout_signs_out_with_a_confirmed_post()
    {
        await SendAsync(HttpMethod.Get, "/test/signin");
        var page = await (await SendAsync(HttpMethod.Get, MiddtIdentityDefaults.LogoutPath)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        page.Should().Contain("admin");

        var response = await SendAsync(HttpMethod.Post, MiddtIdentityDefaults.LogoutPath, HiddenFields(page, "logout"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/");
        (await SendAsync(HttpMethod.Get, MiddtIdentityUi.SecurityPath)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Development_mailbox_lists_messages()
    {
        await app.Services.GetRequiredService<IIdentityMessageSender>().SendAsync("ali@test.com", "Doğrulama kodu", "Kodunuz: 123456", TestContext.Current.CancellationToken);

        var html = WebUtility.HtmlDecode(await (await SendAsync(HttpMethod.Get, MiddtIdentityUi.MailboxPath)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        html.Should().Contain("ali@test.com").And.Contain("Kodunuz: 123456");
    }

    [GeneratedRegex("<form[\\s\\S]*?</form>")]
    private static partial Regex FormRegex();

    [GeneratedRegex("<input type=\"hidden\" name=\"(?<name>[^\"]+)\" value=\"(?<value>[^\"]*)\"")]
    private static partial Regex HiddenRegex();

    private sealed class FakeJourneys : IAuthJourneyService
    {
        private const string Binding = "secret";
        private readonly ConcurrentDictionary<Guid, AuthJourneyState> states = new();

        public ConcurrentQueue<(string Journey, AuthJourneyStart Start)> Starts { get; } = new();

        public ConcurrentQueue<JsonObject> Inputs { get; } = new();

        private static AuthJourneyState Credentials(Guid id, IReadOnlyList<string> errors) => new()
        {
            Id = id,
            Journey = AuthJourneys.Login,
            State = "credentials",
            View = "credentials",
            Errors = errors,
            Actions =
            [
                new AuthJourneyAction
                {
                    Key = "submit-credentials",
                    InputSchema = JsonNode.Parse("""{"type":"object","properties":{"userName":{"type":"string"},"password":{"type":"string"}},"required":["userName","password"]}""")!.AsObject(),
                },
            ],
        };

        public Task<AuthJourneyHandle> StartAsync(string journey, AuthJourneyStart start, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid();
            Starts.Enqueue((journey, start));
            states[id] = Credentials(id, []);
            return Task.FromResult(new AuthJourneyHandle(id, Binding));
        }

        public Task<AuthJourneyState?> GetAsync(Guid id, string binding, CancellationToken cancellationToken = default) =>
            Task.FromResult(binding == Binding && states.TryGetValue(id, out var state) ? state : null);

        public Task<AuthJourneyState?> SubmitAsync(Guid id, string binding, string action, JsonObject? input, CancellationToken cancellationToken = default)
        {
            if (binding != Binding || !states.ContainsKey(id))
            {
                return Task.FromResult<AuthJourneyState?>(null);
            }

            Inputs.Enqueue(input!);
            var state = input?["password"]?.GetValue<string>() == "test"
                ? new AuthJourneyState { Id = id, Journey = AuthJourneys.Login, State = "authenticated", IsFinished = true, IsSucceeded = true }
                : Credentials(id, ["Kullanıcı adı veya şifre hatalı."]);
            states[id] = state;
            return Task.FromResult<AuthJourneyState?>(state);
        }

        public Task<AuthJourneyCompletion?> CompleteAsync(Guid id, string binding, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthJourneyCompletion?>(null);
    }
}
