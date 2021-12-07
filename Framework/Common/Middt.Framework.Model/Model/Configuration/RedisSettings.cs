using System.Collections.Generic;

namespace Middt.Framework.Model.Model.Configuration
{
    public class RedisModel
    {
        public string Host { get; set; }

        public int Port { get; set; }

    }

    public class RedisSettings
    {
        public RedisSettings()
        {
           RedisModelList = new List<RedisModel>();
        }
        public List<RedisModel> RedisModelList { get; set; }
    }
}
