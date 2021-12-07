using RestEase;
using Middt.Framework.Common.Model.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Service
{
    public interface IBaseListRefit<TModel>
                       where TModel : new()
    {

        [Get("/api/v{version}/{controllerName}/getbyid")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<TModel>> GetById([Path] string version, [Path] string controllerName, int id);

        [Post("/api/v{version}/{controllerName}/get")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<TModel>> Get([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] TModel model);

        [Get("/api/v{version}/{controllerName}/getall")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<List<TModel>>> GetAll([Path] string version, [Path] string controllerName);


        [Post("/api/v{version}/{controllerName}/getitems")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<List<TModel>>> GetItems([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<TModel> model);
    }
}
