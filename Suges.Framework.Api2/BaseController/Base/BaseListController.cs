using Microsoft.AspNetCore.Mvc;
using Suges.Framework.Common.Database;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Model.Model.Enumerations;


namespace Suges.Framework.Api
{
    public abstract class BaseListController<TModel, TRepository> : BaseController
        where TModel : class, new()
        where TRepository : BaseRepository<TModel>, new()
    {

        protected TRepository repository;
        protected BaseListController()
        {
            repository = new TRepository();
        }

        public virtual BaseResponseDataModel<TModel> GetById(int id)
        {
            BaseResponseDataModel<TModel> response = new BaseResponseDataModel<TModel>();

            try
            {
                response.Data = repository.GetById(id);

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");

            }
            catch (Exception ex)
            {
                response.Result = ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }


        public virtual BaseResponseDataModel<List<TModel>> GetAll()
        {
            BaseResponseDataModel<List<TModel>> response = new BaseResponseDataModel<List<TModel>>();

            try
            {
                response.Data = repository.GetAll().ToList();

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");

            }
            catch (Exception ex)
            {
                response.Result = ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }



        public virtual BaseResponseDataModel<List<TModel>> GetItems([FromBody] BaseSearchRequestModel<TModel> model)
        {
            BaseResponseDataModel<List<TModel>> response = new BaseResponseDataModel<List<TModel>>();

            try
            {
                response = repository.GetItems(model);

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");

            }
            catch (Exception ex)
            {
                response.Result = ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }
    }
}
