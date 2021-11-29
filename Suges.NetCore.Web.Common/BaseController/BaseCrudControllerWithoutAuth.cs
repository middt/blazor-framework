using Microsoft.AspNetCore.Mvc;
using Suges.Framework.Common.Database;
using Suges.Framework.Common.Model.Data;
using System.Collections.Generic;

namespace Suges.Framework.Api
{
    public class BaseCrudControllerWithoutAuth<TModel, TRepository> : BaseCrudController<TModel, TRepository>
        where TModel : class, new()
        where TRepository : BaseRepository<TModel>, new()
    {
        [HttpGet("[action]")]
        public override BaseResponseDataModel<TModel> GetById(int id)
        {
            return base.GetById(id);
        }

        [HttpGet("[action]")]
        public override BaseResponseDataModel<List<TModel>> GetAll()
        {
            return base.GetAll();
        }

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public override BaseResponseDataModel<List<TModel>> GetItems([FromBody] BaseSearchRequestModel<TModel> model)
        {
            return base.GetItems(model);
        }
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = true)]
        public override BaseResponseDataModel<TModel> Insert([FromBody]TModel model)
        {
            return base.Insert(model);
        }
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = true)]
        public override BaseResponseDataModel<TModel> Update([FromBody]TModel model)
        {
            return base.Update(model);
        }
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public override BaseResponseDataModel<TModel> Delete([FromBody]TModel model)
        {
            return base.Delete(model);
        }
    }
}
