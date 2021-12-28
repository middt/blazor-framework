using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;
using System.Collections.Generic;

namespace Middt.Framework.Api
{

    public abstract class BaseListControllerWithoutAuth<TModel, TRepository> : BaseListController<TModel, TRepository>
        where TModel : class, new()
        where TRepository : IBaseRepository<TModel>, new()
    {
        [HttpGet("[action]")]
        public override Task<BaseResponseDataModel<TModel>> GetById(int id)
        {
            return base.GetById(id);
        }

        [HttpGet("[action]")]
        public override Task<BaseResponseDataModel<List<TModel>>> GetAll()
        {
            return base.GetAll();
        }


        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public override Task<BaseResponseDataModel<List<TModel>>> GetItems([FromBody] BaseSearchRequestModel<TModel> model)
        {
            return base.GetItems(model);
        }



    }
}
