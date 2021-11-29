using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Blazor.Web.Configuration
{
    public class BaseBlazorConfiguration : BaseConfiguration
    {
        protected override void LoadConfig()
        {
            base.LoadConfig();

            IConfigurationSection configurationSection = configurationRoot.GetSection(nameof(GeneralSettings));
            if (configurationSection != null)
            {
                GeneralSettings generalSettings = new GeneralSettings();
                configurationSection.Bind(generalSettings);
                SettingList.Add(generalSettings);
            }

            configurationSection = configurationRoot.GetSection(nameof(ApiSettings));
            if (configurationSection != null)
            {
                ApiSettings dbSettings = new ApiSettings();
                configurationSection.Bind(dbSettings);
                SettingList.Add(dbSettings);
            }


            configurationSection = configurationRoot.GetSection(nameof(SecuritySettings));
            if (configurationSection != null)
            {
                SecuritySettings securitySettings = new SecuritySettings();
                configurationSection.Bind(securitySettings);
                SettingList.Add(securitySettings);
            }

            configurationSection = configurationRoot.GetSection(nameof(DependencySettings));
            if (configurationSection != null)
            {
                DependencySettings dependencySettings = new DependencySettings();
                configurationSection.Bind(dependencySettings);
                SettingList.Add(dependencySettings);
            }

        }
    }

}
