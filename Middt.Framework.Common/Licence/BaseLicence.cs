namespace Middt.Framework.Common.Licence;

public static class BaseLicence
{
    /// <summary>Registers the Syncfusion licence key (call once at startup, before any Syncfusion API is used).</summary>
    public static void Register(string? licence)
    {
        if (!string.IsNullOrEmpty(licence))
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licence);
        }
    }
}
