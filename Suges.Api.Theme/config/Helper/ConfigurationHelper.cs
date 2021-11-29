
using Microsoft.Extensions.Configuration;
using Suges.Framework.Common.Configuration;
using Suges.UBKS.WebApi.config.Model;

namespace Suges.UBKS.WebApi.config.Helper
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
