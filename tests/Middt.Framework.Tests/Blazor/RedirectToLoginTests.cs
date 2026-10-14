using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Web.Base.Component.Security;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Tests.Blazor;

/// <summary>NotAuthorized content of the router (<see cref="RedirectToLogin"/>).</summary>
public sealed class RedirectToLoginTests
{
    [Fact]
    public async Task Signed_in_user_without_the_role_sees_access_denied_instead_of_a_login_redirect()
    {
        // Redirecting to the login page would loop with an identity provider (silent sign-in → same forbidden page).
        var html = await RenderAsync(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user@test.com")], "test"));

        html.Should().Contain("Erişim engellendi").And.NotContain("yönlendiriliyorsunuz");
    }

    [Fact]
    public async Task Anonymous_user_is_sent_to_the_login_page()
    {
        var html = await RenderAsync(new ClaimsIdentity());

        html.Should().Contain("Giriş sayfasına yönlendiriliyorsunuz").And.NotContain("Erişim engellendi");
    }

    private static async Task<string> RenderAsync(ClaimsIdentity identity)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager, TestNavigationManager>()
            .AddSingleton(Options.Create(new SecuritySettings()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingValue<object>.Value)] = state,
                [nameof(CascadingValue<object>.ChildContent)] = (RenderFragment)(builder =>
                {
                    builder.OpenComponent<RedirectToLogin>(0);
                    builder.CloseComponent();
                }),
            }));
            return output.ToHtmlString();
        });
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/ui-admin");
    }
}
