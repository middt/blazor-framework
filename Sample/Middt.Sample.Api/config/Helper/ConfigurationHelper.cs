
using Microsoft.Extensions.Configuration;
using Middt.Framework.Common.Configuration;
using Middt.UBKS.WebApi.config.Model;

namespace Middt.UBKS.WebApi.config.Helper
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
