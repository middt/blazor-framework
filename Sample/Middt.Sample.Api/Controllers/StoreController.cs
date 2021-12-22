using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Middt.Framework.Api;
using Middt.Framework.Api.Cache;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
//using Syncfusion.DocIO;
//using Syncfusion.DocIO.DLS;

namespace Middt.Sample.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class StoreController : BaseCrudControllerWithoutAuth<Store, StoreRepository>
    {
        IBaseCache baseCache;
        public StoreController(IBaseCache _baseCache)
        {
            baseCache = _baseCache;
        }

        [Authorize]
        [HttpGet("[action]")]
        public override Task<BaseResponseDataModel<List<Store>>> GetAll()
        {
            string cacheKey = nameof(CategoryController) + nameof(GetAll);


           Task<BaseResponseDataModel<List<Store>>> result = baseCache.GetSetValue(cacheKey, 1, 1,
                () =>
                {
                    return base.GetAll().Result;
                }
                );

            return result;
        }
    }
}
