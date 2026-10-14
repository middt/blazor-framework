using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Security;
using Middt.Framework.Blazor.Web.DependencyInjection;
using Middt.Framework.Blazor.Web.Pages.Account;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Framework.Tests.Api;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Blazor;

/// <summary>
/// In-process Blazor Server (BFF) host built like an application: <c>AddMiddtBlazor</c> + <c>UseMiddtBlazor</c> on a TestServer.
/// The API token service is a fake (or, optionally, the real <see cref="BaseTokenService"/> against a <see cref="TestApiHost"/>).
/// </summary>
public sealed partial class BlazorTestHost : IAsyncDisposable
{
    public const string WhoAmIPath = "/test/whoami";

    private BlazorTestHost(WebApplication app, FakeTimeProvider time, FakeTokenService tokens)
    {
        App = app;
        Time = time;
        Tokens = tokens;
    }

    public WebApplication App { get; }

    public FakeTimeProvider Time { get; }

    /// <summary>Fake API token service (unused when the host talks to a real API).</summary>
    public FakeTokenService Tokens { get; }

    public IServerTokenStore Store => App.Services.GetRequiredService<IServerTokenStore>();

    /// <summary>
    /// UseMiddtBlazor calls MapStaticAssets, which needs the build's static web assets manifest. A Web SDK app has one;
    /// this (non-web) test assembly gets an empty manifest instead.
    /// </summary>
    private static readonly Lazy<string> StaticAssetsManifest = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, typeof(BlazorTestHost).Assembly.GetName().Name + ".staticwebassets.endpoints.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, """{"Version":1,"ManifestType":"Build","Endpoints":[]}""");
        }

        return path;
    });

    public static async Task<BlazorTestHost> StartAsync(
        string environment = "Development",
        Action<MiddtBlazorOptions>? configure = null,
        TestApiHost? api = null)
    {
        _ = StaticAssetsManifest.Value;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(BlazorTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiSettings:URL"] = "http://localhost",
            ["ApiSettings:Version"] = "1",
            ["GeneralSettings:SessionTimeoutInMinute"] = "10",
            ["GeneralSettings:SessionAbsoluteTimeoutInMinute"] = "60",
        });

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddSingleton<TimeProvider>(time);

        builder.AddMiddtBlazor(o =>
        {
            o.LoginAttemptsPerMinute = 1000;
            configure?.Invoke(o);
        });

        // A Web SDK app gets [ApplicationPart] attributes for referenced Razor class libraries; this test assembly does not.
        builder.Services.AddRazorPages().ConfigureApplicationPartManager(manager =>
        {
            var assembly = typeof(LoginModel).Assembly;
            if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
            {
                // Razor class library: AssemblyPart (page models) + CompiledRazorAssemblyPart (compiled .cshtml).
                foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                {
                    manager.ApplicationParts.Add(part);
                }
            }
        });

        var tokens = new FakeTokenService(_ => new TokenResponseModel { Result = ResultEnum.Error }) { Login = FakeLogin };
        if (api is null)
        {
            builder.Services.AddSingleton<ITokenService>(tokens);
        }
        else
        {
            builder.Services.AddHttpClient<ITokenService, BaseTokenService>()
                .ConfigurePrimaryHttpMessageHandler(() => api.App.GetTestServer().CreateHandler());
        }

        var app = builder.Build();
        app.UseMiddtBlazor(hostPage: "/Account/Login");
        app.MapGet(WhoAmIPath, (HttpContext context) => Results.Ok(new WhoAmI(
            context.User.Identity?.Name,
            context.User.FindFirst(MiddtClaimTypes.SessionId)?.Value,
            [.. context.User.Claims.Select(c => c.Type)])))
            .RequireAuthorization();

        await app.StartAsync();
        return new BlazorTestHost(app, time, tokens);
    }

    public Browser CreateBrowser(string baseAddress = "http://localhost/") => new(App.GetTestClient(), new Uri(baseAddress));

    /// <summary>Issues real JWTs (the cookie claims are read from the access token).</summary>
    private static TokenResponseModel FakeLogin(LoginRequestModel request)
    {
        if (request.username != FakeUserStore.Username || request.password != FakeUserStore.Password)
        {
            return new TokenResponseModel { Result = ResultEnum.Error, MessageList = ["Kullanıcı adı veya şifreniz hatalı."] };
        }

        var generator = new JwtTokenGenerator(
            Options.Create(new JwtSettings { Secret = TestApiHost.Secret, Issuer = TestApiHost.Issuer, Audience = TestApiHost.Audience, Expires = 600 }),
            TimeProvider.System);
        var token = generator.CreateToken(
        [
            new Claim(ClaimTypes.Sid, FakeUserStore.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, FakeUserStore.Username),
            new Claim(ClaimTypes.Role, "1"),
        ]);
        token.Result = ResultEnum.Success;
        token.Username = FakeUserStore.Username;
        return token;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }

    public sealed record WhoAmI(string? Name, string? SessionId, List<string> ClaimTypes);
}

/// <summary>Minimal cookie-aware browser on top of a TestServer client (which neither stores cookies nor follows redirects).</summary>
public sealed partial class Browser(HttpClient client, Uri baseAddress)
{
    public CookieContainer Cookies { get; } = new();

    public Uri BaseAddress => baseAddress;

    public string? CookieValue(string name) => Cookies.GetCookies(baseAddress)[name]?.Value;

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.RequestUri = new Uri(baseAddress, request.RequestUri!);
        var cookieHeader = Cookies.GetCookieHeader(request.RequestUri);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                Cookies.SetCookies(request.RequestUri, setCookie);
            }
        }

        return response;
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        return await SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostFormAsync(string path, IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new FormUrlEncodedContent(fields) };
        return await SendAsync(request);
    }

    /// <summary>GETs a page and returns the antiforgery token of its form.</summary>
    public async Task<string> AntiforgeryTokenAsync(string path)
    {
        using var response = await GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = AntiforgeryField().Match(html);
        match.Success.Should().BeTrue("the form must carry an antiforgery token");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task<HttpResponseMessage> LoginAsync(string username = FakeUserStore.Username, string password = FakeUserStore.Password, string? returnUrl = null)
    {
        var token = await AntiforgeryTokenAsync("/Account/Login");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Username"] = username,
            ["Input.Password"] = password,
        };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return await PostFormAsync("/Account/Login", fields);
    }

    public async Task<HttpResponseMessage> LogoutAsync()
    {
        var token = await AntiforgeryTokenAsync("/Account/Logout");
        return await PostFormAsync("/Account/Logout", [new("__RequestVerificationToken", token)]);
    }

    public async Task<BlazorTestHost.WhoAmI?> WhoAmIAsync()
    {
        using var response = await GetAsync(BlazorTestHost.WhoAmIPath);
        return response.StatusCode == HttpStatusCode.OK
            ? await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<BlazorTestHost.WhoAmI>(response.Content, TestApiHost.Json, TestContext.Current.CancellationToken)
            : null;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
