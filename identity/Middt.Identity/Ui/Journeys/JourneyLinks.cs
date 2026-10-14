using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Middt.Framework.Common.Web;

namespace Middt.Identity.Ui.Journeys;

/// <summary>URL helpers of the account pages (open-redirect safe).</summary>
public static class JourneyLinks
{
    private const string AuthorizePath = "/connect/authorize";

    /// <summary><paramref name="returnUrl"/> when it is a local URL (<c>~/</c> normalised to <c>/</c>), otherwise <c>null</c>.</summary>
    public static string? SafeReturnUrl(string? returnUrl)
    {
        if (!LocalUrl.IsLocal(returnUrl))
        {
            return null;
        }

        return returnUrl!.StartsWith('~') ? returnUrl[1..] : returnUrl;
    }

    /// <summary>
    /// Start data of an anonymous journey. When <paramref name="returnUrl"/> is an OIDC authorization request
    /// (<c>/connect/authorize?client_id=…&amp;scope=…</c>) the client and scopes are passed on (consent, branding).
    /// </summary>
    public static AuthJourneyStart CreateStart(string? returnUrl)
    {
        var safe = SafeReturnUrl(returnUrl);
        if (safe is null)
        {
            return new AuthJourneyStart();
        }

        var queryIndex = safe.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex < 0 ? safe : safe[..queryIndex];
        if (queryIndex < 0 || !string.Equals(path, AuthorizePath, StringComparison.OrdinalIgnoreCase))
        {
            return new AuthJourneyStart { ReturnUrl = safe };
        }

        var query = QueryHelpers.ParseQuery(safe[queryIndex..]);
        var clientId = query.TryGetValue("client_id", out var client) ? client.ToString() : null;
        IReadOnlyList<string> scopes = query.TryGetValue("scope", out var scope)
            ? scope.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new AuthJourneyStart
        {
            ReturnUrl = safe,
            ClientId = string.IsNullOrEmpty(clientId) ? null : clientId,
            Scopes = scopes,
        };
    }

    /// <summary>Page that starts <paramref name="journey"/> again ("Baştan başla" after a failure or expiry).</summary>
    public static string StartPath(string? journey, string? returnUrl)
    {
        var path = journey switch
        {
            AuthJourneys.Register => MiddtIdentityDefaults.RegisterPath,
            AuthJourneys.ResetPassword => MiddtIdentityDefaults.ForgotPasswordPath,
            AuthJourneys.EnrollTotp => MiddtIdentityUi.SecurityPath,
            _ => MiddtIdentityDefaults.LoginPath,
        };

        return WithReturnUrl(path, journey == AuthJourneys.EnrollTotp ? null : returnUrl);
    }

    /// <summary>
    /// Where a journey that ended without success continues: <c>data.redirect</c> when it is a local URL (e.g. the
    /// <c>register</c> / <c>forgot-password</c> actions of <c>auth-login</c>), keeping the journey's return URL.
    /// </summary>
    public static string? FinishedRedirect(AuthJourneyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsFinished || state.IsSucceeded ||
            state.Data["redirect"] is not JsonValue value || !value.TryGetValue<string>(out var redirect) ||
            SafeReturnUrl(redirect) is not { } safe)
        {
            return null;
        }

        return WithReturnUrl(safe, state.ReturnUrl);
    }

    /// <summary><paramref name="path"/> with a <c>returnUrl</c> query parameter when <paramref name="returnUrl"/> is local.</summary>
    public static string WithReturnUrl(string path, string? returnUrl) =>
        SafeReturnUrl(returnUrl) is { } safe && safe != "/" ? QueryHelpers.AddQueryString(path, "returnUrl", safe) : path;
}
