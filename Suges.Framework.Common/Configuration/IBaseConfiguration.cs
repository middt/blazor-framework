using System;
using System.Collections.Generic;

namespace Suges.Framework.Common.Configuration
{
    public abstract class IBaseConfiguration
    {

        protected List<object> SettingList { get; set; }

        public string GetEnviroment()
        {
            return Environment.GetEnvironmentVariable("ENVIRONMENT");
        }
        public IBaseConfiguration()
        {
            SettingList = new List<object>();
        }

        protected virtual void LoadConfig()
        {

        }
        public TClass Get<TClass>()
        {
            object result = SettingList.Find(x => x.GetType().Equals(typeof(TClass)));
            if (result != null)
            {
                return (TClass)result;
            }
            else
            {
                return default(TClass);
            }
        }
    }
}
