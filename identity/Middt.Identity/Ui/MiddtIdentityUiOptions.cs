using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Middt.Identity.Ui;

/// <summary>Marker type of the identity UI assembly (<c>AddAdditionalAssemblies(typeof(MiddtIdentityUi).Assembly)</c>).</summary>
public static class MiddtIdentityUi
{
    /// <summary>Path of the stylesheet served from the library's <c>wwwroot</c>.</summary>
    public const string StylesheetPath = "middt-identity.css";

    /// <summary>Base path of the administration pages.</summary>
    public const string AdminPath = "/admin";

    /// <summary>Development mailbox (only mapped in Development).</summary>
    public const string MailboxPath = "/dev/mailbox";

    public const string SecurityPath = "/account/security";

    public const string DeniedPath = MiddtIdentityDefaults.AccessDeniedPath;

    /// <summary>
    /// Authorization policy of the administration pages: role <see cref="MiddtIdentityOptions.AdminRole"/> on the IdP
    /// cookie scheme. A policy (not <c>[Authorize(AuthenticationSchemes = …)]</c>) because Blazor's route authorization
    /// rejects schemes on components; endpoint authorization still authenticates with the policy's scheme.
    /// </summary>
    public const string AdminPolicy = "Middt.Identity.Admin";

    /// <summary>Authorization policy of signed-in account pages (any user of the IdP cookie scheme).</summary>
    public const string AccountPolicy = "Middt.Identity.Account";
}

/// <summary>Options of the identity UI (<c>services.AddMiddtIdentityUi(o =&gt; …)</c>).</summary>
public sealed class MiddtIdentityUiOptions
{
    /// <summary>Product name shown in the header and the page titles.</summary>
    public string ProductName { get; set; } = "Middt Kimlik";

    /// <summary>Radzen theme of the administration pages (free themes: material, standard, default, humanistic, software).</summary>
    public string AdminTheme { get; set; } = "material";

    /// <summary>Extra assemblies with routable components (host pages) – added to the router and the endpoint mapping.</summary>
    public IList<Assembly> AdditionalAssemblies { get; } = [];

    /// <summary>Workflows listed on the administration "Akışlar" page (custom journeys can be added).</summary>
    public IList<string> AdminJourneyWorkflows { get; } =
        [AuthJourneys.Login, AuthJourneys.Register, AuthJourneys.ResetPassword, AuthJourneys.EnrollTotp];

    internal Dictionary<string, Type> JourneyViews { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers (or replaces) the Razor view of journey states whose <c>view.component</c> is <paramref name="name"/>.
    /// The component receives <see cref="Journeys.JourneyViewBase.State"/> and posts action keys with
    /// <see cref="Components.Shared.JourneyForm"/>.
    /// </summary>
    public MiddtIdentityUiOptions AddJourneyView<TComponent>(string name) where TComponent : IComponent =>
        AddJourneyView(name, typeof(TComponent));

    /// <inheritdoc cref="AddJourneyView{TComponent}(string)"/>
    public MiddtIdentityUiOptions AddJourneyView(string name, Type component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(component);
        if (!typeof(IComponent).IsAssignableFrom(component))
        {
            throw new ArgumentException($"'{component}' is not a Razor component.", nameof(component));
        }

        JourneyViews[name] = component;
        return this;
    }
}
