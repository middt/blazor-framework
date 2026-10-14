using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using OpenIddict.Abstractions;

namespace Middt.Identity.Journeys;

/// <summary>
/// <see cref="IAuthJourneyService"/> on the in-process <see cref="WorkflowEngine"/>. Journeys run as the
/// <see cref="Actor"/> (role <c>idp-journey</c>, which the journey definitions require – a host that also maps the
/// workflow REST endpoints does not expose them). Every call checks the browser binding (SHA-256 of the secret in the
/// journey cookie, constant-time compare); data returned to the UI never contains keys starting with <c>_</c> or the
/// submitted form.
/// </summary>
internal sealed partial class AuthJourneyService(
    WorkflowEngine engine,
    WorkflowDbContext db,
    WorkflowRegistry registry,
    IDataProtectionProvider protection,
    IOptions<MiddtIdentityOptions> options,
    IOpenIddictApplicationManager applications,
    TimeProvider time,
    ILogger<AuthJourneyService> logger) : IAuthJourneyService
{
    /// <summary>Role of the journey actor; the default journeys require it.</summary>
    public const string JourneyRole = "idp-journey";

    public static readonly WorkflowUser Actor = new() { Id = JourneyRole, Name = JourneyRole, Roles = [JourneyRole] };

    public async Task<AuthJourneyHandle> StartAsync(string journey, AuthJourneyStart start, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(journey);
        ArgumentNullException.ThrowIfNull(start);

        var data = new JsonObject();
        foreach (var (key, value) in start.Data ?? [])
        {
            // Extra values (e.g. a prefilled e-mail) never override what the service and the tasks control.
            if (!key.StartsWith('_') && !JourneyData.Reserved.Contains(key) && value is JsonValue)
            {
                data[key] = value.DeepClone();
            }
        }

        var binding = JourneySecrets.NewSecret();
        data[JourneyData.BindingHash] = JourneySecrets.Sha256(binding);
        data[JourneyData.ReturnUrl] = IsLocalUrl(start.ReturnUrl) ? start.ReturnUrl : null;
        // The application's name is shown on the first page already (consent sets it again for consent-only journeys).
        if (start.ClientId is not null && await applications.FindByClientIdAsync(start.ClientId, cancellationToken) is { } application)
        {
            data[JourneyData.ClientName] = await applications.GetLocalizedDisplayNameAsync(application, cancellationToken) ?? start.ClientId;
        }

        switch (journey)
        {
            case AuthJourneys.Login:
                data[JourneyData.ClientId] = start.ClientId;
                data[JourneyData.Scopes] = new JsonArray([.. start.Scopes.Select(s => JsonValue.Create(s))]);
                if (start.UserId is not null)
                {
                    // Already signed in: only the consent step (started by the authorize endpoint).
                    data[JourneyData.UserId] = start.UserId;
                    data[JourneyData.ConsentOnly] = true;
                    data[JourneyData.ForceConsent] = start.Data?[JourneyData.ForceConsent] is JsonValue force && force.TryGetValue<bool>(out var f) && f;
                }

                break;
            case AuthJourneys.Register:
                data[JourneyData.ClientId] = start.ClientId;
                data[JourneyData.Scopes] = new JsonArray([.. start.Scopes.Select(s => JsonValue.Create(s))]);
                data["registrationAllowed"] = options.Value.AllowRegistration;
                data["requireConfirmedEmail"] = options.Value.RequireConfirmedEmail;
                break;
            case AuthJourneys.EnrollTotp:
                data[JourneyData.UserId] = start.UserId ?? throw new ArgumentException("TOTP enrollment needs the signed-in user (UserId).", nameof(start));
                break;
            default:
                if (start.UserId is not null)
                {
                    data[JourneyData.UserId] = start.UserId;
                }

                break;
        }

        var (instance, _) = await engine.StartAsync(journey, new StartWorkflowRequest { Data = data }, Actor, cancellationToken);
        return new AuthJourneyHandle(instance.Id, binding);
    }

    public async Task<AuthJourneyState?> GetAsync(Guid id, string binding, CancellationToken cancellationToken = default)
    {
        var instance = await FindBoundAsync(id, binding, cancellationToken);
        return instance is null ? null : ToState(instance, []);
    }

    public async Task<AuthJourneyState?> SubmitAsync(Guid id, string binding, string action, JsonObject? input, CancellationToken cancellationToken = default)
    {
        var instance = await FindBoundAsync(id, binding, cancellationToken);
        if (instance is null)
        {
            return null;
        }

        var transition = instance.AvailableTransitions.FirstOrDefault(t => string.Equals(t.Key, action, StringComparison.Ordinal));
        if (transition is null)
        {
            return ToState(instance, [JourneyTexts.NotAvailable]);
        }

        try
        {
            var result = await engine.ExecuteTransitionAsync(id, action, input, Actor, runAsync: false, cancellationToken);
            return ToState(result, result.Status == WorkflowInstanceStatus.Faulted ? [JourneyTexts.Unexpected] : []);
        }
        catch (WorkflowValidationException ex)
        {
            return ToState(instance, [.. ex.Errors.Select(e => JourneyTexts.Validation(e, transition.InputSchema)).Distinct()]);
        }
        catch (WorkflowConflictException)
        {
            return ToState(await engine.GetAsync(id, Actor, cancellationToken), [JourneyTexts.Busy]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not WorkflowException)
        {
            // The engine faulted the instance (sensitive input is not kept, so it cannot be retried): start over.
            LogJourneyFailed(logger, ex, id);
            return ToState(await engine.GetAsync(id, Actor, cancellationToken), [JourneyTexts.Unexpected]);
        }
    }

    public async Task<AuthJourneyCompletion?> CompleteAsync(Guid id, string binding, CancellationToken cancellationToken = default)
    {
        var row = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (row is null || row.Status != WorkflowInstanceStatus.Completed || row.CompletedAt is not { } completedAt
            || completedAt < time.GetUtcNow().UtcDateTime - options.Value.JourneyLifetime)
        {
            return null;
        }

        var data = JsonPath.ParseObject(row.Data);
        if (!IsBound(data, binding) || JourneyTask.Flag(data, JourneyData.Consumed)
            || registry.Get(row.WorkflowKey, row.WorkflowVersion).FindState(row.State)?.SubType != StateSubType.Success)
        {
            return null;
        }

        // One time: compare-and-swap on the revision, so two concurrent requests cannot both complete the journey.
        data[JourneyData.Consumed] = true;
        var json = data.ToJsonString();
        var now = time.GetUtcNow().UtcDateTime;
        var revision = row.Revision;
        var updated = await db.Instances
            .Where(i => i.Id == id && i.Revision == revision && i.Status == WorkflowInstanceStatus.Completed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Data, json)
                .SetProperty(i => i.Revision, revision + 1)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            return null;
        }

        var consentOnly = JourneyTask.Flag(data, JourneyData.ConsentOnly);
        var returnUrl = JourneyTask.Text(data, JourneyData.ReturnUrl);
        if (JourneyTask.Flag(data, JourneyData.ConsentDenied) && returnUrl is not null)
        {
            // The authorize endpoint answers access_denied (forging the marker only denies your own request).
            returnUrl += (returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + MiddtIdentityEndpoints.ConsentDeniedParameter + "=1";
        }

        // Login/registration sign in; TOTP enrollment re-issues the session (the security stamp changed).
        var signIn = (row.WorkflowKey is AuthJourneys.Login or AuthJourneys.Register && !consentOnly) || row.WorkflowKey == AuthJourneys.EnrollTotp;
        return new AuthJourneyCompletion(row.WorkflowKey, JourneyTask.Text(data, JourneyData.UserId), returnUrl, signIn);
    }

    /// <summary>The instance when it exists, is bound to <paramref name="binding"/> and is neither expired nor consumed.</summary>
    private async Task<WorkflowInstanceModel?> FindBoundAsync(Guid id, string binding, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(binding))
        {
            return null;
        }

        WorkflowInstanceModel instance;
        try
        {
            instance = await engine.GetAsync(id, Actor, cancellationToken);
        }
        catch (Exception ex) when (ex is WorkflowNotFoundException or WorkflowForbiddenException)
        {
            return null;
        }

        var expired = instance.StateSubType == nameof(StateSubType.Timeout)
            || (instance.Status is not (WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
                && instance.CreatedAt < time.GetUtcNow().UtcDateTime - options.Value.JourneyLifetime);
        return IsBound(instance.Data, binding) && !expired && !JourneyTask.Flag(instance.Data, JourneyData.Consumed) ? instance : null;
    }

    private static bool IsBound(JsonObject data, string binding) =>
        !string.IsNullOrEmpty(binding) && JourneySecrets.HashEquals(JourneyTask.Text(data, JourneyData.BindingHash), JourneySecrets.Sha256(binding));

    private AuthJourneyState ToState(WorkflowInstanceModel instance, IReadOnlyList<string> errors)
    {
        var finished = instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Faulted;
        var data = UiData(instance);
        var allErrors = errors.Count > 0 ? errors
            : JourneyTask.Text(instance.Data, JourneyData.Error) is { } error ? [error]
            : [];

        return new AuthJourneyState
        {
            Id = instance.Id,
            Journey = instance.Workflow,
            State = instance.State,
            StateTitle = instance.StateTitle,
            View = instance.View,
            Data = data,
            Actions = instance.Status == WorkflowInstanceStatus.Active
                ? [.. instance.AvailableTransitions.Select(t => new AuthJourneyAction { Key = t.Key, Title = t.Title, InputSchema = t.InputSchema })]
                : [],
            Errors = allErrors,
            IsFinished = finished,
            IsSucceeded = instance.Status == WorkflowInstanceStatus.Completed && instance.StateSubType == nameof(StateSubType.Success),
            ReturnUrl = JourneyTask.Text(instance.Data, JourneyData.ReturnUrl),
        };
    }

    /// <summary>UI-safe data: no internal (<c>_</c>) keys, no submitted form; the TOTP key only while it is being set up.</summary>
    private JsonObject UiData(WorkflowInstanceModel instance)
    {
        var data = new JsonObject();
        foreach (var (key, value) in instance.Data)
        {
            if (!key.StartsWith('_') && key != JourneyData.Form && key != JourneyData.Error)
            {
                data[key] = value?.DeepClone();
            }
        }

        if (instance.View == "totp-setup" && instance.Status == WorkflowInstanceStatus.Active
            && JourneyTask.Text(instance.Data, JourneyData.TotpKeyProtected) is { } protectedKey)
        {
            var key = protection.CreateProtector(TotpSetupTask.Purpose).Unprotect(protectedKey);
            var account = JourneyTask.Text(instance.Data, JourneyData.Email) ?? JourneyTask.Text(instance.Data, JourneyData.UserName) ?? "user";
            var issuer = options.Value.AuthenticatorIssuerName;
            data[JourneyData.TotpKey] = string.Join(' ', key.Chunk(4).Select(c => new string(c))).ToLowerInvariant();
            data[JourneyData.TotpUri] = string.Create(CultureInfo.InvariantCulture,
                $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6");
        }

        return data;
    }

    /// <summary>Same rules as <c>IUrlHelper.IsLocalUrl</c>: <c>/path</c> but not <c>//host</c> or <c>/\host</c>.</summary>
    internal static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authentication journey {JourneyId} failed unexpectedly")]
    private static partial void LogJourneyFailed(ILogger logger, Exception exception, Guid journeyId);
}
