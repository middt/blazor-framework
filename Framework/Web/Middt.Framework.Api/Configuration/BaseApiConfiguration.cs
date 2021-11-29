using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Common.Configuration
{
    public class BaseApiConfiguration : BaseConfiguration
    {
        protected override void LoadConfig()
        {
            IConfigurationSection configurationSection = configurationRoot.GetSection(nameof(JwtSettings));
            if (configurationSection != null)
            {
                JwtSettings jwtSettings = new JwtSettings();
                configurationSection.Bind(jwtSettings);
                SettingList.Add(jwtSettings);
            }

            configurationSection = configurationRoot.GetSection(nameof(CorsSettings));
            if (configurationSection != null)
            {
                CorsSettings corsSettings = new CorsSettings();
                configurationSection.Bind(corsSettings);
                SettingList.Add(corsSettings);
            }


            configurationSection = configurationRoot.GetSection(nameof(EmailSettings));
            if (configurationSection != null)
            {
                EmailSettings emailSettings = new EmailSettings();
                configurationSection.Bind(emailSettings);
                SettingList.Add(emailSettings);
            }
            configurationSection = configurationRoot.GetSection(nameof(UygulamaSettings));
            if (configurationSection != null)
            {
                UygulamaSettings uygulamaSettings = new UygulamaSettings();
                configurationSection.Bind(uygulamaSettings);
                SettingList.Add(uygulamaSettings);
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
