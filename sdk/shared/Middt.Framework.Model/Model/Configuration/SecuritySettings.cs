namespace Middt.Framework.Model.Model.Configuration;

public class SecuritySettings
{
    public const string SectionName = nameof(SecuritySettings);

    /// <summary>Server rendered login page (cookie sign-in), provided by Middt.Framework.Blazor.Web.</summary>
    public string LoginPageURL { get; set; } = "/Account/Login";

    public string LogoutPageURL { get; set; } = "/Account/Logout";

    /// <summary>
    /// Page shown when a signed-in user lacks a required role. Not the login page: with an identity provider (single
    /// sign-on) the login page would sign the user in again and return to the forbidden page – an endless loop.
    /// </summary>
    public string AccessDeniedPageURL { get; set; } = "/Account/AccessDenied";
}
