using System;

namespace Middt.Framework.Api.Cache
{
    public interface IBaseCache
    {
        Task<T> GetSetValue<T>(string cacheKey, int SlidingExpiration, int AbsoluteExpiration, Func<T> func);
    }
}
