using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
using System.Collections.Generic;

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
        public virtual BaseResponseDataModel<TModel> GetById(string version, int id)
        {
            return ExecutePolly(() =>
            {
                return api.GetById(version, controllerName, id).Result;
            }
);
        }

        public virtual BaseResponseDataModel<TModel> GetById(int id)
        {
            return GetById("1.0", id);
        }



        public virtual BaseResponseDataModel<TModel> Get(string version, TModel model)
        {
            return ExecutePolly(() =>
            {
                return api.Get(version, controllerName, model).Result;
            }
);
        }
        public virtual BaseResponseDataModel<TModel> Get(TModel model)
        {
            return Get("1.0", model);
        }



        public virtual BaseResponseDataModel<List<TModel>> GetAll(string version)
        {
            return ExecutePolly(() =>
            {
                return api.GetAll(version, controllerName).Result;
            }
);
        }
        public virtual BaseResponseDataModel<List<TModel>> GetAll()
        {
            return GetAll("1.0");
        }

        public virtual BaseResponseDataModel<List<TModel>> GetItems(string version, BaseSearchRequestModel<TModel> model)
        {
            return ExecutePolly(() =>
            {
                return api.GetItems(version, controllerName, model).Result;
            }
);
        }

        public virtual BaseResponseDataModel<List<TModel>> GetItems(BaseSearchRequestModel<TModel> model)
        {
            return GetItems("1.0", model);
        }
    }
}

