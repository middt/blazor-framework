using RestEase;

using Suges.Framework.Common.Model.Data;
using System.Threading.Tasks;

namespace Suges.Framework.Common.Service
{
    public interface IBaseCrudRefit<TModel> : IBaseListRefit<TModel>
                where TModel : new()
    {

        [Post("/api/v{version}/{controllerName}/insert")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<TModel>> Insert([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] TModel model);

        [Post("/api/v{version}/{controllerName}/update")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<TModel>> Update([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] TModel model);

        [Post("/api/v{version}/{controllerName}/delete")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<TModel>> Delete([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] TModel model);
    }
}
