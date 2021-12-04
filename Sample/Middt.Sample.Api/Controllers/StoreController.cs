using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Middt.Framework.Api;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
//using Syncfusion.DocIO;
//using Syncfusion.DocIO.DLS;

namespace Middt.Sample.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class StoreController : BaseCrudControllerWithoutAuth<Store, StoreRepository>
    {
        IDistributedCache distributedCache;
        public StoreController(IDistributedCache _distributedCache)
        {
            distributedCache = _distributedCache;
        }

        [Authorize]
        [HttpGet("[action]")]
        public override BaseResponseDataModel<List<Store>> GetAll()
        {
            BaseResponseDataModel<List<Store>> result = null;

            string cacheKey = nameof(CategoryController) + nameof(GetAll);


            byte[] cacheValue = distributedCache.GetAsync(cacheKey).Result;
            string jsonValue = string.Empty;

            if (cacheValue != null)
            {
                jsonValue = Encoding.UTF8.GetString(cacheValue);
                result = JsonConvert.DeserializeObject<BaseResponseDataModel<List<Store>>>(jsonValue);
            }
            else
            {
                result = base.GetAll();
                jsonValue = JsonConvert.SerializeObject(result);
                cacheValue = Encoding.UTF8.GetBytes(jsonValue);
                var options = new DistributedCacheEntryOptions()
                        .SetSlidingExpiration(TimeSpan.FromDays(1))
                        .SetAbsoluteExpiration(DateTime.Now.AddMonths(1));


                distributedCache.SetAsync(cacheKey, cacheValue, options);
            }


            return result;
        }
    }
}
