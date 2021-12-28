using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Service
{
    public abstract class BaseCrudRefit<TInterface, TModel> : BaseListRefit<TInterface, TModel>
              where TInterface : IBaseCrudRefit<TModel>
              where TModel : new()
    {
        public BaseCrudRefit(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }

        public async Task<BaseResponseDataModel<TModel>> Insert(string version, TModel model)
        {
            return await  ExecutePolly(() =>
            {
                return api.Insert(version, controllerName, model).Result;
            }
);
        }
        public async Task<BaseResponseDataModel<TModel>> Insert(TModel model)
        {
            return await  Insert("1.0", model);
        }


        public async Task<BaseResponseDataModel<TModel>> Update(string version, TModel model)
        {
            return await  ExecutePolly(() =>
            {
                return api.Update(version, controllerName, model).Result;
            });
        }

        public async Task<BaseResponseDataModel<TModel>> Update(TModel model)
        {
            return await  Update("1.0", model);
        }


        public async Task<BaseResponseDataModel<TModel>> Delete(string version, TModel model)
        {
            return await  ExecutePolly(() =>
            {
                return api.Delete(version, controllerName, model).Result;
            });
        }
        public async Task<BaseResponseDataModel<TModel>> Delete(TModel model)
        {
            return await Delete("1.0", model);
        }
    }
}
