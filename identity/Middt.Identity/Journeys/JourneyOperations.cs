using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Middt.Framework.Workflow.Tasks;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity.Journeys;

internal interface IIdentityJourneyOperation
{
    Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Helpers shared by the journey operations. Business failures (wrong password/code) never throw: they set <c>data.error</c>
/// and flags, and the journey's automatic transitions route back to the step (an exception would fault the journey).
/// </summary>
internal static class JourneyTask
{
    public static string? Text(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    /// <summary>Sensitive values are read raw (passwords may start/end with spaces).</summary>
    public static string? Secret(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    public static bool Flag(JsonObject data, string key) => data[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    public static List<string> Strings(JsonObject data, string key) =>
        data[key] is JsonArray array ? [.. array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()] : [];

    /// <summary>Counts a failed attempt of this journey and ends it (<c>attemptsExceeded</c>) after the configured maximum.</summary>
    public static void Fail(JsonObject data, string error, MiddtIdentityOptions options)
    {
        var attempts = (data[JourneyData.FailedAttempts] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0) + 1;
        data[JourneyData.FailedAttempts] = attempts;
        data[JourneyData.AttemptsExceeded] = attempts >= options.MaxJourneyAttempts;
        data[JourneyData.Error] = error;
    }

    public static void Succeed(JsonObject data) => data.Remove(JourneyData.Error);

    public static async Task<MiddtIdentityUser?> FindUserAsync(UserManager<MiddtIdentityUser> users, string? userNameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(userNameOrEmail))
        {
            return null;
        }

        return await users.FindByNameAsync(userNameOrEmail)
            ?? (userNameOrEmail.Contains('@', StringComparison.Ordinal) ? await users.FindByEmailAsync(userNameOrEmail) : null);
    }

    public static string NameOf(MiddtIdentityUser user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName ?? user.Id : user.DisplayName;
}

/// <summary>
/// <c>idp-verify-credentials</c>: checks <c>input.userName</c> (user name or e-mail) / <c>input.password</c> with lockout
/// (wrong passwords count via <c>AccessFailedAsync</c>). Sets <c>authenticated</c>, <c>userId</c>, <c>userName</c>,
/// <c>displayName</c>, <c>mfaRequired</c>; on failure <c>error</c>, <c>failedAttempts</c>, <c>attemptsExceeded</c>.
/// </summary>
internal sealed class VerifyCredentialsOperation(UserManager<MiddtIdentityUser> users, IOptions<MiddtIdentityOptions> options) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["authenticated"] = false;
        var user = await JourneyTask.FindUserAsync(users, JourneyTask.Text(context.Input, "userName"));
        var password = JourneyTask.Secret(context.Input, "password") ?? string.Empty;

        if (user is null)
        {
            // Same work as for an existing user: response time does not reveal which user names exist.
            users.PasswordHasher.VerifyHashedPassword(new MiddtIdentityUser(), DummyHash.Value, password);
            JourneyTask.Fail(data, JourneyTexts.InvalidCredentials, options.Value);
            return;
        }

        if (await users.IsLockedOutAsync(user))
        {
            JourneyTask.Fail(data, JourneyTexts.LockedOut, options.Value);
            return;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            JourneyTask.Fail(data, await users.IsLockedOutAsync(user) ? JourneyTexts.LockedOut : JourneyTexts.InvalidCredentials, options.Value);
            return;
        }

        if (options.Value.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            data[JourneyData.Error] = JourneyTexts.EmailNotConfirmed;
            return;
        }

        await users.ResetAccessFailedCountAsync(user);
        JourneyTask.Succeed(data);
        data["authenticated"] = true;
        data[JourneyData.UserId] = user.Id;
        data[JourneyData.UserName] = user.UserName;
        data[JourneyData.DisplayName] = JourneyTask.NameOf(user);
        data["mfaRequired"] = user.TwoFactorEnabled && !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user));
    }

    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<MiddtIdentityUser>().HashPassword(new MiddtIdentityUser(), JourneySecrets.NewSecret()));
}

/// <summary><c>idp-verify-totp</c>: verifies <c>input.code</c> against the authenticator of <c>data.userId</c> (counts for lockout). Sets <c>mfaPassed</c>.</summary>
internal sealed class VerifyTotpOperation(UserManager<MiddtIdentityUser> users, IOptions<MiddtIdentityOptions> options) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["mfaPassed"] = false;
        var user = JourneyTask.Text(data, JourneyData.UserId) is { } id ? await users.FindByIdAsync(id) : null;
        if (user is null)
        {
            JourneyTask.Fail(data, JourneyTexts.UserNotFound, options.Value);
            data[JourneyData.AttemptsExceeded] = true;
            return;
        }

        if (await users.IsLockedOutAsync(user))
        {
            JourneyTask.Fail(data, JourneyTexts.LockedOut, options.Value);
            return;
        }

        var code = JourneyTask.Secret(context.Input, "code")?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            await users.AccessFailedAsync(user);
            JourneyTask.Fail(data, await users.IsLockedOutAsync(user) ? JourneyTexts.LockedOut : JourneyTexts.InvalidTotp, options.Value);
            return;
        }

        await users.ResetAccessFailedCountAsync(user);
        JourneyTask.Succeed(data);
        data["mfaPassed"] = true;
    }
}

/// <summary>
/// <c>idp-check-consent</c>: for <c>data.clientId</c> and <c>data.scopes</c> sets <c>clientName</c>, <c>scopeDetails</c> and
/// <c>consentRequired</c> (explicit/systematic consent clients without a permanent authorization covering the scopes, or
/// <c>data.forceConsent</c>).
/// </summary>
internal sealed class CheckConsentOperation(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictScopeManager scopes) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["consentRequired"] = false;
        var clientId = JourneyTask.Text(data, JourneyData.ClientId);
        var application = clientId is null ? null : await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return;
        }

        var requested = JourneyTask.Strings(data, JourneyData.Scopes);
        data[JourneyData.ClientName] = await applications.GetLocalizedDisplayNameAsync(application, cancellationToken) ?? clientId;
        var details = new JsonArray();
        foreach (var scope in requested)
        {
            details.Add(new JsonObject { ["name"] = scope, ["displayName"] = await ScopeDisplayNameAsync(scopes, scope, cancellationToken) });
        }

        data[JourneyData.ScopeDetails] = details;

        var consentType = await applications.GetConsentTypeAsync(application, cancellationToken);
        if (consentType is not (ConsentTypes.Explicit or ConsentTypes.Systematic))
        {
            return;
        }

        // Systematic consent is treated like explicit consent (asked once, again with prompt=consent).
        if (JourneyTask.Flag(data, JourneyData.ForceConsent))
        {
            data["consentRequired"] = true;
            return;
        }

        var subject = JourneyTask.Text(data, JourneyData.UserId) ?? string.Empty;
        var applicationId = await applications.GetIdAsync(application, cancellationToken) ?? string.Empty;
        var existing = false;
        await foreach (var _ in authorizations.FindAsync(subject, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, [.. requested], cancellationToken))
        {
            existing = true;
            break;
        }

        data["consentRequired"] = !existing;
    }

    internal static async Task<string> ScopeDisplayNameAsync(IOpenIddictScopeManager scopes, string scope, CancellationToken cancellationToken) => scope switch
    {
        Scopes.OpenId => "Kimlik bilginiz",
        Scopes.Profile => "Profil bilgileriniz (ad, kullanıcı adı)",
        Scopes.Email => "E-posta adresiniz",
        Scopes.Roles => "Rolleriniz",
        Scopes.OfflineAccess => "Siz yokken oturumunuzun yenilenmesi",
        _ => await scopes.FindByNameAsync(scope, cancellationToken) is { } entity
            ? await scopes.GetLocalizedDisplayNameAsync(entity, cancellationToken) ?? scope
            : scope,
    };
}

/// <summary><c>idp-grant-consent</c>: stores a permanent authorization of <c>data.userId</c> for <c>data.clientId</c>/<c>data.scopes</c>.</summary>
internal sealed class GrantConsentOperation(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        var subject = JourneyTask.Text(data, JourneyData.UserId) ?? throw new InvalidOperationException("The journey has no user.");
        var clientId = JourneyTask.Text(data, JourneyData.ClientId) ?? throw new InvalidOperationException("The journey has no client.");
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new InvalidOperationException($"Client '{clientId}' does not exist.");

        var identity = new ClaimsIdentity("Middt.Identity.Consent", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, subject);
        var scopes = JourneyTask.Strings(data, JourneyData.Scopes).ToImmutableArray();
        identity.SetScopes(scopes);
        await authorizations.CreateAsync(identity, subject, await applications.GetIdAsync(application, cancellationToken) ?? string.Empty,
            AuthorizationTypes.Permanent, scopes, cancellationToken);
        data["consentRequired"] = false;
        JourneyTask.Succeed(data);
    }
}

/// <summary>
/// <c>idp-create-user</c>: creates the user from <c>input.userName/email/displayName/password</c> (password policy applies).
/// Sets <c>created</c>, <c>userId</c>, <c>email</c> or <c>error</c>. Honours <c>AllowRegistration</c> and <c>RequireConfirmedEmail</c>.
/// </summary>
internal sealed class CreateUserOperation(UserManager<MiddtIdentityUser> users, IOptions<MiddtIdentityOptions> options, TimeProvider time) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["created"] = false;
        if (!options.Value.AllowRegistration)
        {
            data[JourneyData.Error] = JourneyTexts.RegistrationDisabled;
            return;
        }

        var input = context.Input;
        var user = new MiddtIdentityUser
        {
            UserName = JourneyTask.Text(input, "userName"),
            Email = JourneyTask.Text(input, "email"),
            DisplayName = JourneyTask.Text(input, "displayName"),
            EmailConfirmed = !options.Value.RequireConfirmedEmail,
            CreatedAt = time.GetUtcNow(),
        };

        var result = await users.CreateAsync(user, JourneyTask.Secret(input, "password") ?? string.Empty);
        if (!result.Succeeded)
        {
            data[JourneyData.Error] = JourneyTexts.Describe(result.Errors, users.Options);
            return;
        }

        JourneyTask.Succeed(data);
        data["created"] = true;
        data[JourneyData.UserId] = user.Id;
        data[JourneyData.UserName] = user.UserName;
        data[JourneyData.DisplayName] = JourneyTask.NameOf(user);
        data[JourneyData.Email] = user.Email;
    }
}

/// <summary>
/// <c>idp-send-email-code</c>: sends a 6 digit code and stores only its salted SHA-256 with an expiry. Config:
/// <c>purpose</c> = <c>confirm-email</c> (user <c>data.userId</c>) or <c>reset-password</c> (address <c>input.email</c>; nothing is
/// sent for unknown addresses but the journey behaves the same), <c>resend: true</c> for repeated requests (limited).
/// </summary>
internal sealed class SendEmailCodeOperation(
    UserManager<MiddtIdentityUser> users,
    IIdentityMessageSender sender,
    IOptions<MiddtIdentityOptions> options,
    TimeProvider time) : IIdentityJourneyOperation
{
    private const int MaxResends = 5;

    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        if (context.Config["resend"] is JsonValue resend && resend.TryGetValue<bool>(out var isResend) && isResend)
        {
            var count = (data["resendCount"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0) + 1;
            data["resendCount"] = count;
            if (count > MaxResends)
            {
                data[JourneyData.Error] = JourneyTexts.TooManyCodes;
                return;
            }
        }

        MiddtIdentityUser? user;
        string? email;
        if (context.Config["purpose"]?.GetValue<string>() == "reset-password")
        {
            email = JourneyTask.Text(context.Input, "email") ?? JourneyTask.Text(data, "codeSentTo");
            user = email is null ? null : await users.FindByEmailAsync(email);
        }
        else
        {
            user = JourneyTask.Text(data, JourneyData.UserId) is { } id ? await users.FindByIdAsync(id) : null;
            email = user?.Email;
        }

        var code = JourneySecrets.NewNumericCode();
        var salt = JourneySecrets.NewSecret();
        // Unknown address: a hash nobody knows the code of – verification fails exactly like a wrong code.
        data[JourneyData.CodeHash] = JourneySecrets.Sha256(salt + ":" + (user is null ? JourneySecrets.NewSecret() : code));
        data[JourneyData.CodeSalt] = salt;
        data[JourneyData.CodeExpiresAt] = time.GetUtcNow().Add(options.Value.EmailCodeLifetime).ToString("O", CultureInfo.InvariantCulture);
        data[JourneyData.CodeUserId] = user?.Id;
        data["codeSentTo"] = email;
        data["codeAttempts"] = 0;
        JourneyTask.Succeed(data);

        if (user is not null && !string.IsNullOrEmpty(user.Email))
        {
            await sender.SendAsync(user.Email, JourneyTexts.EmailCodeSubject, JourneyTexts.EmailCodeText(code, options.Value.EmailCodeLifetime), cancellationToken);
        }
    }
}

/// <summary>
/// <c>idp-verify-email-code</c>: compares <c>input.code</c> with the stored hash (constant time, expiry, single use). Sets
/// <c>codeValid</c>; with config <c>confirmEmail: true</c> the user's e-mail is marked confirmed.
/// </summary>
internal sealed class VerifyEmailCodeOperation(UserManager<MiddtIdentityUser> users, IOptions<MiddtIdentityOptions> options, TimeProvider time) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["codeValid"] = false;
        var code = JourneyTask.Secret(context.Input, "code")?.Trim() ?? string.Empty;
        var expires = JourneyTask.Text(data, JourneyData.CodeExpiresAt) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : DateTimeOffset.MinValue;
        var salt = JourneyTask.Text(data, JourneyData.CodeSalt) ?? string.Empty;
        var matches = JourneySecrets.HashEquals(JourneyTask.Text(data, JourneyData.CodeHash), JourneySecrets.Sha256(salt + ":" + code));
        var userId = JourneyTask.Text(data, JourneyData.CodeUserId);

        if (!matches || expires < time.GetUtcNow() || userId is null)
        {
            JourneyTask.Fail(data, JourneyTexts.InvalidEmailCode, options.Value);
            return;
        }

        // Single use.
        data.Remove(JourneyData.CodeHash);
        data.Remove(JourneyData.CodeSalt);
        data.Remove(JourneyData.CodeExpiresAt);

        if (context.Config["confirmEmail"] is JsonValue confirm && confirm.TryGetValue<bool>(out var confirmEmail) && confirmEmail &&
            await users.FindByIdAsync(userId) is { EmailConfirmed: false } user)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        JourneyTask.Succeed(data);
        data["codeValid"] = true;
    }
}

/// <summary><c>idp-reset-password</c>: after a verified code sets <c>input.password</c> (policy applies), unlocks the account. Sets <c>passwordReset</c>.</summary>
internal sealed class ResetPasswordOperation(UserManager<MiddtIdentityUser> users) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["passwordReset"] = false;
        var user = JourneyTask.Flag(data, "codeValid") && JourneyTask.Text(data, JourneyData.CodeUserId) is { } id ? await users.FindByIdAsync(id) : null;
        if (user is null)
        {
            data[JourneyData.Error] = JourneyTexts.InvalidEmailCode;
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, JourneyTask.Secret(context.Input, "password") ?? string.Empty);
        if (!result.Succeeded)
        {
            data[JourneyData.Error] = JourneyTexts.Describe(result.Errors, users.Options);
            return;
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true; // the code proved ownership of the address
            await users.UpdateAsync(user);
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        JourneyTask.Succeed(data);
        data["passwordReset"] = true;
        data["codeValid"] = false;
    }
}

/// <summary>
/// <c>idp-totp-setup</c>: creates a new authenticator key for <c>data.userId</c>, stored encrypted (Data Protection) in the
/// journey until it is confirmed; the UI gets <c>totpKey</c>/<c>totpUri</c> only while the journey shows the setup view.
/// </summary>
internal sealed class TotpSetupOperation(UserManager<MiddtIdentityUser> users, IDataProtectionProvider protection) : IIdentityJourneyOperation
{
    internal const string Purpose = "Middt.Identity.Journeys.TotpKey";

    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        var user = JourneyTask.Text(data, JourneyData.UserId) is { } id ? await users.FindByIdAsync(id) : null;
        if (user is null)
        {
            throw new InvalidOperationException("TOTP enrollment needs an existing user.");
        }

        data[JourneyData.TotpKeyProtected] = protection.CreateProtector(Purpose).Protect(Totp.NewKey());
        data[JourneyData.UserName] = user.UserName;
        data[JourneyData.DisplayName] = JourneyTask.NameOf(user);
        data[JourneyData.Email] = user.Email;
    }
}

/// <summary><c>idp-enable-totp</c>: verifies <c>input.code</c> against the pending key, stores the key and enables two-factor sign-in. Sets <c>totpEnabled</c>.</summary>
internal sealed class EnableTotpOperation(
    UserManager<MiddtIdentityUser> users,
    IUserStore<MiddtIdentityUser> store,
    IDataProtectionProvider protection,
    IOptions<MiddtIdentityOptions> options,
    TimeProvider time) : IIdentityJourneyOperation
{
    public async Task ExecuteAsync(WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        var data = context.Data;
        data["totpEnabled"] = false;
        var user = JourneyTask.Text(data, JourneyData.UserId) is { } id ? await users.FindByIdAsync(id) : null;
        var protectedKey = JourneyTask.Text(data, JourneyData.TotpKeyProtected);
        if (user is null || protectedKey is null || store is not IUserAuthenticatorKeyStore<MiddtIdentityUser> keys)
        {
            JourneyTask.Fail(data, JourneyTexts.UserNotFound, options.Value);
            data[JourneyData.AttemptsExceeded] = true;
            return;
        }

        var key = protection.CreateProtector(TotpSetupOperation.Purpose).Unprotect(protectedKey);
        if (!Totp.Verify(key, JourneyTask.Secret(context.Input, "code"), time.GetUtcNow()))
        {
            JourneyTask.Fail(data, JourneyTexts.InvalidTotp, options.Value);
            return;
        }

        await keys.SetAuthenticatorKeyAsync(user, key, cancellationToken);
        var result = await users.SetTwoFactorEnabledAsync(user, true); // saves the key as well
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Enabling two-factor authentication failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        data.Remove(JourneyData.TotpKeyProtected);
        JourneyTask.Succeed(data);
        data["totpEnabled"] = true;
    }
}
