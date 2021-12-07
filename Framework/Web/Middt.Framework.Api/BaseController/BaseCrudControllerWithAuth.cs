using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Api
{
    public abstract class BaseCrudControllerWithAuth<TModel, TRepository> : BaseCrudController<TModel, TRepository>
        where TModel : class, new()
        where TRepository : BaseRepository<TModel>, new()
    {
        [Authorize]
        [HttpGet("[action]")]
        public override BaseResponseDataModel<TModel> GetById(int id)
        {
            return base.GetById(id);
        }


        [Authorize]
        [HttpGet("[action]")]
        public override BaseResponseDataModel<List<TModel>> GetAll()
        {
            return base.GetAll();
        }

        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public override BaseResponseDataModel<List<TModel>> GetItems([FromBody] BaseSearchRequestModel<TModel> model)
        {
            return base.GetItems(model);
        }
        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = true)]
        public override BaseResponseDataModel<TModel> Insert([FromBody] TModel model)
        {
            return base.Insert(model);
        }
        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = true)]
        public override BaseResponseDataModel<TModel> Update([FromBody] TModel model)
        {
            return base.Update(model);
        }
        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public override BaseResponseDataModel<TModel> Delete([FromBody] TModel model)
        {
            return base.Delete(model);
        }


    }
}
