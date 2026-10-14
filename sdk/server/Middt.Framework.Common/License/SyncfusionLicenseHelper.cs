namespace Middt.Framework.Common.License;

public static class SyncfusionLicenseHelper
{
    /// <summary>Registers the Syncfusion license key (call once at startup, before any Syncfusion API is used).</summary>
    public static void Register(string? license)
    {
        if (!string.IsNullOrEmpty(license))
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(license);
        }
    }
}
