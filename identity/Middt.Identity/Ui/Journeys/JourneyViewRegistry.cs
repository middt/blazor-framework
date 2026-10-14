using Microsoft.Extensions.Options;
using Middt.Identity.Ui.Components.Account.Views;

namespace Middt.Identity.Ui.Journeys;

/// <summary>Names of the journey views shipped with the UI (workflow state <c>view.component</c>).</summary>
public static class JourneyViewNames
{
    public const string Credentials = "credentials";
    public const string Totp = "totp";
    public const string Consent = "consent";
    public const string Register = "register";
    public const string EmailCode = "email-code";
    public const string ResetRequest = "reset-request";
    public const string NewPassword = "new-password";
    public const string TotpSetup = "totp-setup";
}

/// <summary>
/// Maps journey view names to Razor components: built-in views, overridden or extended by
/// <see cref="MiddtIdentityUiOptions.AddJourneyView{TComponent}"/>; unknown names render <see cref="GenericJourneyView"/>.
/// </summary>
public sealed class JourneyViewRegistry
{
    private readonly Dictionary<string, Type> views;

    public JourneyViewRegistry(IOptions<MiddtIdentityUiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        views = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            [JourneyViewNames.Credentials] = typeof(CredentialsView),
            [JourneyViewNames.Totp] = typeof(TotpView),
            [JourneyViewNames.Consent] = typeof(ConsentView),
            [JourneyViewNames.Register] = typeof(RegisterView),
            [JourneyViewNames.EmailCode] = typeof(EmailCodeView),
            [JourneyViewNames.ResetRequest] = typeof(ResetRequestView),
            [JourneyViewNames.NewPassword] = typeof(NewPasswordView),
            [JourneyViewNames.TotpSetup] = typeof(TotpSetupView),
        };

        foreach (var (name, type) in options.Value.JourneyViews)
        {
            views[name] = type;
        }
    }

    /// <summary>Registered view names.</summary>
    public IReadOnlyCollection<string> Names => views.Keys;

    /// <summary>The component for <paramref name="view"/>; <see cref="GenericJourneyView"/> when none is registered.</summary>
    public Type Resolve(string? view) =>
        !string.IsNullOrEmpty(view) && views.TryGetValue(view, out var type) ? type : typeof(GenericJourneyView);
}
