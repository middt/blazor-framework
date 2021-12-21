using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System;
using System.Text;

namespace Middt.Framework.Api.Cache
{
    public class BaseDistributedCache : IBaseCache
    {
        IDistributedCache distributedCache;
        public BaseDistributedCache(IDistributedCache _distributedCache)
        {
            distributedCache = _distributedCache;
        }
        public async Task<T> GetSetValue<T>(string cacheKey, int SlidingExpiration, int AbsoluteExpiration, Func<T> func)
        {
            T result = default(T);

            byte[] cacheValue = distributedCache.GetAsync(cacheKey).Result;
            string jsonValue = string.Empty;

            if (cacheValue != null)
            {
                jsonValue = Encoding.UTF8.GetString(cacheValue);
                result = JsonConvert.DeserializeObject<T>(jsonValue);
            }
            else
            {
                result = func();
                jsonValue = JsonConvert.SerializeObject(result);
                cacheValue = Encoding.UTF8.GetBytes(jsonValue);
                var options = new DistributedCacheEntryOptions()
                        .SetSlidingExpiration(TimeSpan.FromDays(SlidingExpiration))
                        .SetAbsoluteExpiration(DateTime.Now.AddMonths(AbsoluteExpiration));


               await distributedCache.SetAsync(cacheKey, cacheValue, options);
            }

            return result;
        }
    }
}