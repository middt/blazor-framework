
using Microsoft.Extensions.Configuration;
using Middt.Framework.Common.Configuration;
using Middt.Sample.Api.config.Model;

namespace Middt.Sample.Api.config.Helper
{
    public class ConfigurationHelper : BaseApiConfiguration
    {
        protected override void LoadConfig()
        {
            base.LoadConfig();

            IConfigurationSection configurationSection = configurationRoot.GetSection(nameof(DBSettings));
            if (configurationSection != null)
            {
                DBSettings dbSettings = new DBSettings();
                configurationSection.Bind(dbSettings);
                SettingList.Add(dbSettings);
            }

        }
    }
}
