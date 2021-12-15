using Middt.Framework.Common.Configuration;
using Middt.Framework.Model.Model.Configuration;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public class MiddtRedisConnection
    {
        public ConnectionMultiplexer connectionMultiplexer { get; set; }

        public  RedLockFactory redLockFactory { get; set; }

        RedisSettings redisSettings;
        public MiddtRedisConnection(RedisSettings _redisSettings)
        {
            redisSettings = _redisSettings;
            ConfigurationOptions configurationOptions = GetConfig();
            CreateConnection(configurationOptions);
            CreateLock();
        }

        private void CreateLock()
        {
            redLockFactory = RedLockFactory.Create(new List<RedLockMultiplexer>
            {
                connectionMultiplexer
            } );
        }
        private void CreateConnection(ConfigurationOptions configurationOptions)
        {
            connectionMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);
        }

        private ConfigurationOptions GetConfig()
        {
            ConfigurationOptions configurationOptions = new ConfigurationOptions();
            configurationOptions.SyncTimeout = 500000;
            configurationOptions.AbortOnConnectFail = false;

            List<RedisModel> redisModelList = redisSettings.RedisModelList;

            foreach (RedisModel redisModel in redisModelList)
            {
                configurationOptions.EndPoints.Add(redisModel.Host, redisModel.Port);
            }
            return configurationOptions;
        }
    }
}
