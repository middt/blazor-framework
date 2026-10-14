using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Bff.Security;

namespace Middt.Framework.Blazor.Web.Security;

/// <summary>
/// Authentication state of a Blazor Server circuit, seeded from the auth cookie of the initial HTTP request and
/// revalidated periodically against the server side session: a session that was signed out elsewhere, revoked or
/// timed out ends the circuit's authenticated state as well.
/// </summary>
public sealed class MiddtAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    /// <summary>Immediately signs the current circuit out (e.g. after the API rejected the refresh token).</summary>
    public void SignOutCircuit() =>
        SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        if (authenticationState.User.FindFirst(MiddtClaimTypes.SessionId)?.Value is not { } sessionId)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IServerTokenStore>();
        return await store.GetAsync(sessionId, touch: false, cancellationToken) is not null;
    }
}
