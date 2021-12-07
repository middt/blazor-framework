﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Middt.Framework.Api;
using Middt.Framework.Api.Cache;
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
        IBaseCache baseCache;
        public StaffController(IBaseCache _baseCache)
        {
            baseCache = _baseCache;
        }

        [Authorize]
        [HttpGet("[action]")]
        public override BaseResponseDataModel<List<Staff>> GetAll()
        {
            string cacheKey = nameof(StaffController) + nameof(GetAll);

            BaseResponseDataModel<List<Staff>> result = baseCache.GetSetValue<BaseResponseDataModel<List<Staff>>>(cacheKey, 1, 1,
                () =>
                {
                    return base.GetAll();
                }
                );

            return result;
        }
    }
}
