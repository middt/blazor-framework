using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Middt.Framework.Common.Configuration
{
    public class BaseConfiguration : IBaseConfiguration
    {
        protected List<object> SettingList = new ();
        protected ConfigurationManager configurationRoot = new();

        public BaseConfiguration()
        {
               configurationRoot.SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
                     .AddJsonFile($"config/appsettings.json", true, true)
                     .AddJsonFile($"config/appsettings.{GetEnviroment()}.json", true, true)
                     .AddEnvironmentVariables();
        }

        public TClass Get<TClass>()
        {
            object result = configurationRoot.GetSection(typeof(TClass).Name).Get<TClass>();
            //object result = SettingList.Find(x => x.GetType().Equals(typeof(TClass)));
            if (result != null)
            {
                return (TClass)result;
            }
            else
            {
                return default(TClass);
            }
        }

        public string GetEnviroment()
        {
            return Environment.GetEnvironmentVariable("ENVIRONMENT");
        }
    }
}
