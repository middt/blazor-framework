using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Middt.Framework.Blazor.Web.Extensions;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Blazor.Web.Security;

/// <summary>
/// <see cref="IBaseSessionState"/> for the BFF model: resolves the session id from the authenticated circuit user
/// and reads/writes the API tokens in <see cref="IServerTokenStore"/>.
/// </summary>
public sealed class ServerSessionState(
    AuthenticationStateProvider authenticationStateProvider,
    IServerTokenStore tokenStore,
    NavigationManager navigationManager,
    IOptions<SecuritySettings> securitySettings) : IBaseSessionState
{
    public async Task<string> Token() => (await GetAsync())?.access_token ?? string.Empty;

    public async Task<string> RefreshToken() => (await GetAsync())?.refresh_token ?? string.Empty;

    public async Task<int> ExpiresIn() => (await GetAsync())?.expires_in ?? -1;

    public async Task SetToken(TokenResponseModel tokenResponseModel)
    {
        if (await SessionIdAsync() is not { } sessionId)
        {
            return;
        }

        // Keep the absolute expiry of the session: refreshing tokens must not extend it. A session that ended meanwhile
        // (signed out elsewhere, idle or absolute timeout) must not be recreated with a fresh lifetime.
        if (await tokenStore.GetAsync(sessionId, touch: true) is not { } existing)
        {
            return;
        }

        await tokenStore.SetAsync(sessionId, new SaveTokenResponseModel(tokenResponseModel) { expire_datetime = existing.expire_datetime });
    }

    public async Task ClearToken()
    {
        if (await SessionIdAsync() is { } sessionId)
        {
            await tokenStore.RemoveAsync(sessionId);
        }

        if (authenticationStateProvider is MiddtAuthenticationStateProvider provider)
        {
            provider.SignOutCircuit();
        }

        try
        {
            navigationManager.NavigateToLogin(securitySettings.Value.LoginPageURL);
        }
        catch (InvalidOperationException)
        {
            // Not running inside an interactive circuit (no navigation possible); the cookie check signs out on the next request.
        }
    }

    private async Task<SaveTokenResponseModel?> GetAsync() =>
        await SessionIdAsync() is { } sessionId ? await tokenStore.GetAsync(sessionId, touch: true) : null;

    private async Task<string?> SessionIdAsync()
    {
        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            return state.User.FindFirst(MiddtClaimTypes.SessionId)?.Value;
        }
        catch (InvalidOperationException)
        {
            // Authentication state not initialised (outside a circuit).
            return null;
        }
    }
}
