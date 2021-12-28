using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Service
{
    public abstract class BaseListRefit<TInterface, TModel> : BaseRefit<TInterface>
               where TInterface : IBaseListRefit<TModel>
               where TModel : new()
    {
        public BaseListRefit(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {
        }
        public virtual async Task<BaseResponseDataModel<TModel>> GetById(string version, int id)
        {
            return await ExecutePolly(() =>
            {
                return api.GetById(version, controllerName, id).Result;
            }
);
        }

        public async Task<BaseResponseDataModel<TModel>> GetById(int id)
        {
            return await GetById("1.0", id);
        }



        public async Task<BaseResponseDataModel<TModel>> Get(string version, TModel model)
        {
            return await ExecutePolly(() =>
            {
                return api.Get(version, controllerName, model).Result;
            }
);
        }
        public async Task<BaseResponseDataModel<TModel>> Get(TModel model)
        {
            return await Get("1.0", model);
        }



        public async Task<BaseResponseDataModel<List<TModel>>> GetAll(string version)
        {
            return await ExecutePolly(() =>
            {
                return api.GetAll(version, controllerName).Result;
            }
);
        }
        public async Task<BaseResponseDataModel<List<TModel>>> GetAll()
        {
            return await GetAll("1.0");
        }

        public async Task<BaseResponseDataModel<List<TModel>> >GetItems(string version, BaseSearchRequestModel<TModel> model)
        {
            return await ExecutePolly(() =>
            {
                return api.GetItems(version, controllerName, model).Result;
            }
);
        }

        public async Task<BaseResponseDataModel<List<TModel>>> GetItems(BaseSearchRequestModel<TModel> model)
        {
            return await GetItems("1.0", model);
        }
    }
}

