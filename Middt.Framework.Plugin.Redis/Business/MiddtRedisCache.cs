
using Middt.Framework.Common.Helper;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public class MiddtRedisCache
    {
        TimeSpan lockWait = TimeSpan.FromSeconds(10);
        TimeSpan lockRetry = TimeSpan.FromSeconds(1);

        public int DBIndex { get; set; } = 0;


        MiddtRedisConnection baseRedisConnection;
        MiddtRedislock baseRedislock;

        public MiddtRedisCache(MiddtRedisConnection _baseRedisConnection, MiddtRedislock _baseRedislock)
        {
            baseRedisConnection = _baseRedisConnection;
            baseRedislock = _baseRedislock;
        }

        public async Task<bool> WriteWithLock<T>(string cacheName, Func<T> RunMethod, int lockTimeOutSecond, int cacheTimeOutSecond)
        {
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            TimeSpan cacheTimeout = TimeSpan.FromSeconds(cacheTimeOutSecond);

            string lockName = cacheName + "_Lock";


            using (var redLock = baseRedislock.CreateLock(lockName, lockTimeout, lockWait, lockRetry)) // there are also non async Create() methods
            {
                if (redLock.Result.IsAcquired)
                {
                    T result = RunMethod();

                    JsonHelper<T> jsonHelper = new JsonHelper<T>();
                    string json = jsonHelper.SerializeObject(result);

                    IDatabase database = GetDatabase();
                    await database.StringSetAsync(cacheName, json, cacheTimeout);
                }
            }
            return true;
        }
        public async Task<T> ReadWrite<T>(string cacheName, Func<T> RunMethod, int lockTimeOutSecond, int cacheTimeOutSecond)
        {
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            TimeSpan cacheTimeout = TimeSpan.FromSeconds(cacheTimeOutSecond);

            string lockName = cacheName + "_Lock";


            T returnModel = Read<T>(cacheName);

            if (returnModel != null && !returnModel.Equals(default(T)))
                return returnModel;


            returnModel = baseRedislock.ExecuteMethod<T>(() =>
            {
                T result = Read<T>(cacheName);


                if (result != null && !result.Equals(default(T)))
                    return returnModel;

                result = RunMethod();

                JsonHelper<T> jsonHelper = new JsonHelper<T>();
                string json = jsonHelper.SerializeObject(result);

                IDatabase database = GetDatabase();
                database.StringSetAsync(cacheName, json, cacheTimeout).Wait();

                return result;
            }, lockName, lockTimeout, lockWait, lockRetry);

            return returnModel;

        }

        public void DeleteKey(string cacheName, int lockTimeOutSecond)
        {
            string lockName = cacheName + "_Lock";
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            using (var redLock = baseRedislock.CreateLock(lockName, lockTimeout, lockWait, lockRetry)) // there are also non async Create() methods
            {
                if (redLock.Result.IsAcquired)
                {
                    IDatabase database = GetDatabase();
                    database.KeyDeleteAsync(cacheName);
                }
            }
        }

        private T Deserialize<T>(string value)
        {
            T result = default(T);
            if (!string.IsNullOrEmpty(value))
            {
                JsonHelper<T> jsonHelper = new JsonHelper<T>();
                result = jsonHelper.DeserializeObject(value);
            }

            return result;
        }

        public T Read<T>(string cacheName)
        {
            IDatabase database = GetDatabase();
            RedisValue result = database.StringGetAsync(cacheName).Result;
            return Deserialize<T>(result);
        }

        private IDatabase GetDatabase()
        {
            IConnectionMultiplexer redisClient = baseRedisConnection.connectionMultiplexer;
            return redisClient.GetDatabase(DBIndex);
        }
    }
}
