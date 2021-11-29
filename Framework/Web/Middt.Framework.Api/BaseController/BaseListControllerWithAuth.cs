using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Api
{

    public abstract class BaseListControllerWithAuth<TModel, TRepository> : BaseListController<TModel, TRepository>
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

    }
}
