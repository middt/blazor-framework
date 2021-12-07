namespace Middt.Framework.Common.Licence
{
    public class BaseLicence
    {
        public void Register(string licence)
        {
            if (!string.IsNullOrEmpty(licence))
                Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licence);
        }
    }
}
