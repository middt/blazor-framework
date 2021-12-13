
using Middt.Framework.Common.Helper;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public abstract class BaseRedisCache<T> where T : class
    {
        TimeSpan lockWait = TimeSpan.FromSeconds(10);
        TimeSpan lockRetry = TimeSpan.FromSeconds(1);

        public int DBIndex { get; set; } = 0;

        public abstract string CacheName { get; }

        protected string LockName
        {
            get
            {
                return CacheName + "_Lock";
            }
        }

        private readonly JsonHelper<T> jsonHelper;

        BaseRedisConnection baseRedisConnection;
        BaseRedislock baseRedislock;

        public BaseRedisCache(BaseRedisConnection _baseRedisConnection, BaseRedislock _baseRedislock)
        {
            baseRedisConnection = _baseRedisConnection;
            baseRedislock = _baseRedislock;


            jsonHelper = new JsonHelper<T>();
        }

        public bool WriteWithLock(Func<T> RunMethod, int lockTimeOutSecond)
        {
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            using (var redLock = baseRedislock.CreateLock(LockName, lockTimeout, lockWait, lockRetry)) // there are also non async Create() methods
            {
                if (redLock.IsAcquired)
                {
                    T result = RunMethod();
                    string json = jsonHelper.SerializeObject(result);

                    IDatabase database = GetDatabase();
                    database.StringSet(CacheName, json);
                }
            }
            return true;
        }
        public T ReadWrite(Func<T> RunMethod, int lockTimeOutSecond, int cacheTimeOutSecond)
        {
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            TimeSpan cacheTimeout = TimeSpan.FromSeconds(cacheTimeOutSecond);


            T returnModel = Read();

            if (returnModel != null)
                return returnModel;


            returnModel = baseRedislock.ExecuteMethod<T>(() =>
            {
                T result = Read();

                if (result != null)
                    return result;

                result = RunMethod();
                string json = jsonHelper.SerializeObject(result);

                IDatabase database = GetDatabase();
                database.StringSet(CacheName, json, cacheTimeout);

                return result;
            }, LockName, lockTimeout, lockWait, lockRetry);

            return returnModel;

        }

        public void DeleteKey(int lockTimeOutSecond)
        {
            TimeSpan lockTimeout = TimeSpan.FromSeconds(lockTimeOutSecond);
            using (var redLock = baseRedislock.CreateLock(LockName, lockTimeout, lockWait, lockRetry)) // there are also non async Create() methods
            {
                if (redLock.IsAcquired)
                {
                    IDatabase database = GetDatabase();
                    database.KeyDelete(CacheName);
                }
            }

        }

        private T Deserialize(string value)
        {
            T result = default(T);
            if (!string.IsNullOrEmpty(value))
            {
                result = jsonHelper.DeserializeObject(value);
            }

            return result;
        }

        public T Read()
        {
            IDatabase database = GetDatabase();
            RedisValue result = database.StringGet(CacheName);
            return Deserialize(result);
        }

        private IDatabase GetDatabase()
        {
            IConnectionMultiplexer redisClient = baseRedisConnection.connectionMultiplexer;
            return redisClient.GetDatabase(DBIndex);
        }
    }
}
