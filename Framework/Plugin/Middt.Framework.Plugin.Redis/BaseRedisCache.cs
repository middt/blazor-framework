
using Middt.Framework.Common.Helper;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public class BaseRedisCache<T> where T : class
    {
        TimeSpan wait = TimeSpan.FromSeconds(10);
        TimeSpan retry = TimeSpan.FromSeconds(1);

        TimeSpan timeout = TimeSpan.FromSeconds(30);



        public string CacheName { get; set; } = typeof(T).Name;

        public string LockName
        {
            get
            {
                return CacheName + "_Lock";
            }
        }


        public int DBIndex { get; set; } = 0;



        private readonly JsonHelper<T> jsonHelper;

        BaseRedisConnection baseRedisConnection;
        BaseRedislock baseRedislock;

        public BaseRedisCache(BaseRedisConnection _baseRedisConnection, BaseRedislock _baseRedislock)
        {
            baseRedisConnection = _baseRedisConnection;
            baseRedislock = _baseRedislock;


            jsonHelper = new JsonHelper<T>();
        }

        public bool WriteWithLock(Func<T> RunMethod, int timeOutSecond)
        {
            timeout = TimeSpan.FromSeconds(timeOutSecond);
            using (var redLock = baseRedislock.CreateLock(LockName, timeout, wait, retry)) // there are also non async Create() methods
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
        public bool WriteWithLock(Func<T> RunMethod)
        {

            using (var redLock = baseRedislock.CreateLock(LockName, timeout, wait, retry)) // there are also non async Create() methods
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

        public T ReadWrite(Func<T> RunMethod, int timeOutSecond)
        {
            timeout = TimeSpan.FromSeconds(timeOutSecond);

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
                database.StringSet(CacheName, json, timeout);

                return result;
            }, LockName, timeout, wait, retry);

            return returnModel;

        }

        public void DeleteKey(int timeOutSecond)
        {
            timeout = TimeSpan.FromSeconds(timeOutSecond);
            using (var redLock = baseRedislock.CreateLock(LockName, timeout, wait, retry)) // there are also non async Create() methods
            {
                if (redLock.IsAcquired)
                {
                    IDatabase database = GetDatabase();
                    database.KeyDelete(CacheName);
                }
            }

        }
        public T ReadWrite(Func<T> RunMethod)
        {
            T returnModel;

            returnModel = Read();

            if (returnModel != null)
                return returnModel;


            using (var redLock = baseRedislock.CreateLock(LockName, timeout, wait, retry)) // there are also non async Create() methods
            {
                if (redLock.IsAcquired)
                {
                    returnModel = Read();

                    if (returnModel != null)
                        return returnModel;


                    T result = RunMethod();
                    string json = jsonHelper.SerializeObject(result);

                    IDatabase database = GetDatabase();
                    database.StringSet(CacheName, json);
                    returnModel = result;
                }
            }

            return returnModel;
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
