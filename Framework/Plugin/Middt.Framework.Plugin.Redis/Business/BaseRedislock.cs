using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public class BaseRedislock
    {
        private BaseRedisConnection baseRedisConnection;
        private IBaseLog baseLog;
        public BaseRedislock(BaseRedisConnection _baseRedisConnection, IBaseLog _baseLog)
        {
            baseLog = _baseLog;
            baseRedisConnection = _baseRedisConnection;
        }
        public IRedLock CreateLock(string resource, TimeSpan expiryTime)
        {
            return baseRedisConnection.redLockFactory.CreateLock(resource, expiryTime);
        }
        public IRedLock CreateLock(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null)
        {
            return baseRedisConnection.redLockFactory.CreateLock(resource, expiryTime, waitTime, retryTime, cancellationToken);
        }



        public virtual T ExecuteMethod<T>(Func<T> action, string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null)
        {
            T result = default(T);
            try
            {
                using (var redLock = baseRedisConnection.redLockFactory.CreateLock(resource, expiryTime, waitTime, retryTime)) // there are also non async Create() methods
                {
                    if (redLock.IsAcquired)
                    {
                        try
                        {
                            result = Task.Run<T>(action).Result;
                        }
                        catch (Exception ex)
                        {
                            baseLog.Error("RedLockHelper - ExecuteMethod  - 1.1  " + ex.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                baseLog.Error("RedLockHelper - ExecuteMethod - 1.2 " + ex.ToString());
            }

            return result;
        }

        public virtual async void ExecuteMethod(Action action, string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null)
        {
            try
            {
                using (var redLock = baseRedisConnection.redLockFactory.CreateLock(resource, expiryTime, waitTime, retryTime)) // there are also non async Create() methods
                {
                    if (redLock.IsAcquired)
                    {
                        try
                        {
                            await Task.Run(action);
                        }
                        catch (Exception ex)
                        {
                            baseLog.Error("RedLockHelper - ExecuteMethod  - 2.1  " + ex.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                baseLog.Error("RedLockHelper - ExecuteMethod  - 2.2  " + ex.ToString());
            }
        }
    }
}
