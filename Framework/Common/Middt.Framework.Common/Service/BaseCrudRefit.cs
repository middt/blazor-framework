using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
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

        public virtual BaseResponseDataModel<TModel> Insert(string version, TModel model)
        {
            return ExecutePolly(() =>
            {
                return api.Insert(version, controllerName, model).Result;
            }
);
        }
        public virtual BaseResponseDataModel<TModel> Insert(TModel model)
        {
            return Insert("1.0", model);
        }


        public virtual BaseResponseDataModel<TModel> Update(string version, TModel model)
        {
            return ExecutePolly(() =>
            {
                return api.Update(version, controllerName, model).Result;
            });
        }

        public virtual BaseResponseDataModel<TModel> Update(TModel model)
        {
            return Update("1.0", model);
        }


        public virtual BaseResponseDataModel<TModel> Delete(string version, TModel model)
        {
            return ExecutePolly(() =>
            {
                return api.Delete(version, controllerName, model).Result;
            });
        }
        public virtual BaseResponseDataModel<TModel> Delete(TModel model)
        {
            return Delete("1.0", model);
        }
    }
}
