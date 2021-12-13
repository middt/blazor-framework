using Middt.Framework.Common.Model.Data;
using Middt.Framework.Plugin.Redis;
using Middt.Sample.Api.Model.Database;
using System.Collections.Generic;

namespace Middt.Sample.Api
{
    public class StaffRedisCache : BaseRedisCache<BaseResponseDataModel<List<Staff>>>
    {
        public override string CacheName => "StaffRedisCache";
        public StaffRedisCache(BaseRedisConnection _baseRedisConnection, BaseRedislock _baseRedislock) : base(_baseRedisConnection, _baseRedislock)
        {
        }
    }
}
