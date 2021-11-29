﻿using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository;
//using Syncfusion.DocIO;
//using Syncfusion.DocIO.DLS;
using System.Collections.Generic;

namespace Middt.Sample.Api.Controllers
{
    [ApiController]
    //[Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class Table1Controller : BaseCrudControllerWithoutAuth<Table1, Table1Repository>
    {

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]

        public BaseResponseDataModel<List<Table1>> GetRandevuByFirmaID([FromBody] BaseSearchRequestModel<Table1> model)
        {
            return repository.GetItems(model);
        }

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]

        public BaseResponseDataModel<Table1> GetFirmaByFirmaID()
        {
            BaseResponseDataModel<Table1> response = new BaseResponseDataModel<Table1>();
            response.Result = Framework.Model.Model.Enumerations.ResultEnum.Success;
            response.Data = repository.FirstOrDefault(x => x.Table1Id > 0);

            return response;
        }


    }
}
