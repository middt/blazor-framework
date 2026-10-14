using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;

namespace Middt.Identity;

/// <summary>A user of the identity provider (ASP.NET Core Identity, schema <c>idp</c>).</summary>
public class MiddtIdentityUser : IdentityUser
{
    /// <summary>Name shown in the UI and issued as the <c>name</c> claim (falls back to <see cref="IdentityUser.UserName"/>).</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>Built-in authentication journeys (workflow definitions shipped with the package, overridable by key).</summary>
public static class AuthJourneys
{
    /// <summary>Sign-in: credentials → optional TOTP → optional consent → authenticated.</summary>
    public const string Login = "auth-login";

    /// <summary>Self registration: details → e-mail verification code → registered (signed in).</summary>
    public const string Register = "auth-register";

    /// <summary>Forgotten password: e-mail → verification code → new password → done.</summary>
    public const string ResetPassword = "auth-reset-password";

    /// <summary>Authenticator (TOTP) enrollment for a signed-in user: show key/QR → confirm code → enabled.</summary>
    public const string EnrollTotp = "auth-enroll-totp";
}

/// <summary>How a journey is started.</summary>
public sealed class AuthJourneyStart
{
    /// <summary>Local URL to continue with after a successful journey (e.g. the OIDC authorize request).</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>OIDC client of the authorization request (consent, branding), if any.</summary>
    public string? ClientId { get; init; }

    /// <summary>Scopes requested by the client (consent).</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Signed-in user for journeys that need one (e.g. TOTP enrollment); <c>null</c> for anonymous journeys.</summary>
    public string? UserId { get; init; }

    /// <summary>Extra start data (e.g. a prefilled e-mail); never put secrets here.</summary>
    public JsonObject? Data { get; init; }
}

/// <summary>A started journey: the id plus the browser binding secret (stored in an HttpOnly cookie, hashed in the instance).</summary>
public sealed record AuthJourneyHandle(Guid Id, string Binding);

/// <summary>One action the user can take in the current step.</summary>
public sealed class AuthJourneyAction
{
    public string Key { get; init; } = string.Empty;

    public string? Title { get; init; }

    /// <summary>JSON Schema of the expected input (sensitive fields included – they are not persisted).</summary>
    public JsonObject? InputSchema { get; init; }
}

/// <summary>
/// The state of a journey as the UI sees it. <see cref="Data"/> contains only UI-safe values (no binding hash, no
/// secrets): e.g. <c>userName</c>, <c>clientName</c>, <c>scopes</c>, <c>totpUri</c> during enrollment.
/// </summary>
public sealed class AuthJourneyState
{
    public Guid Id { get; init; }

    public string Journey { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string? StateTitle { get; init; }

    /// <summary>Razor journey view to render for this state (workflow <c>view.component</c>), e.g. <c>credentials</c>.</summary>
    public string? View { get; init; }

    public JsonObject Data { get; init; } = [];

    public IReadOnlyList<AuthJourneyAction> Actions { get; init; } = [];

    /// <summary>Messages of the last submit (validation or e.g. "Kullanıcı adı veya şifre hatalı.").</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The journey reached a final state.</summary>
    public bool IsFinished { get; init; }

    /// <summary>The final state is a success (<c>subType: Success</c>); <see cref="IAuthJourneyService.CompleteAsync"/> can be called.</summary>
    public bool IsSucceeded { get; init; }

    public string? ReturnUrl { get; init; }
}

/// <summary>Result of completing a successful journey (one time).</summary>
public sealed record AuthJourneyCompletion(string Journey, string? UserId, string? ReturnUrl, bool SignIn);

/// <summary>
/// Runs authentication journeys on the workflow engine. Every call is bound to the browser that started the journey
/// (<see cref="AuthJourneyHandle.Binding"/>): a leaked journey id alone is useless.
/// </summary>
public interface IAuthJourneyService
{
    Task<AuthJourneyHandle> StartAsync(string journey, AuthJourneyStart start, CancellationToken cancellationToken = default);

    /// <summary>Current state, or <c>null</c> when the journey does not exist, expired or the binding does not match.</summary>
    Task<AuthJourneyState?> GetAsync(Guid id, string binding, CancellationToken cancellationToken = default);

    /// <summary>Executes an action of the current step; validation/business errors are returned in <see cref="AuthJourneyState.Errors"/>.</summary>
    Task<AuthJourneyState?> SubmitAsync(Guid id, string binding, string action, JsonObject? input, CancellationToken cancellationToken = default);

    /// <summary>Consumes a succeeded journey once; <c>null</c> when not succeeded, already consumed or not bound to this browser.</summary>
    Task<AuthJourneyCompletion?> CompleteAsync(Guid id, string binding, CancellationToken cancellationToken = default);
}

/// <summary>Delivers e-mail messages of journeys (verification codes). The demo uses a development mailbox.</summary>
public interface IIdentityMessageSender
{
    Task SendAsync(string email, string subject, string text, CancellationToken cancellationToken = default);
}
