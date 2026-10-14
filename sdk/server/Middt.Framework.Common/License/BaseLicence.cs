namespace Middt.Framework.Common.Licence;

[Obsolete("Use Middt.Framework.Common.License.SyncfusionLicenseHelper instead.")]
public static class BaseLicence
{
    public static void Register(string? licence) =>
        Middt.Framework.Common.License.SyncfusionLicenseHelper.Register(licence);
}
