using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Middt.Framework.Api;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
using System;
using System.Collections.Generic;
//using Syncfusion.DocIO;
//using Syncfusion.DocIO.DLS;

namespace Middt.Sample.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class StaffController : BaseCrudControllerWithAuth<Staff, StaffRepository>
    {
        IMemoryCache memoryCache;
        public StaffController(IMemoryCache _memoryCache)
        {
            memoryCache = _memoryCache;
        }

        [Authorize]
        [HttpGet("[action]")]
        public override BaseResponseDataModel<List<Staff>> GetAll()
        {
            BaseResponseDataModel<List<Staff>> result = null;

            string cacheKey = nameof(StaffController) + nameof(GetAll);
            if (!memoryCache.TryGetValue(cacheKey, out result))
            {
                result = base.GetAll();

                memoryCache.Set(
                    cacheKey,
                    result,
                    new MemoryCacheEntryOptions()
                    {
                        AbsoluteExpiration = DateTime.Now.AddHours(1),
                        SlidingExpiration = TimeSpan.FromMinutes(5)
                    });
            }
            return result;
        }
    }
}
