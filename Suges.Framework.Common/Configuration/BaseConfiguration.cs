using Microsoft.Extensions.Configuration;
using System.IO;
using System.Reflection;

namespace Suges.Framework.Common.Configuration
{
    public class BaseConfiguration : IBaseConfiguration
    {
        protected IConfigurationRoot configurationRoot;


        public BaseConfiguration()
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
               // .SetBasePath(Directory.GetCurrentDirectory())
               .SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
                     .AddJsonFile($"config/appsettings.json", true, true)
                     .AddJsonFile($"config/appsettings.{GetEnviroment()}.json", true, true)
                     .AddEnvironmentVariables();

            configurationRoot = configurationBuilder.Build();

            LoadConfig();
        }
    }
}
