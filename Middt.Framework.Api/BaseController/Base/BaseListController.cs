using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Middt.Framework.Api
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

        public virtual async Task<BaseResponseDataModel<TModel>> GetById(int id)
        {
            BaseResponseDataModel<TModel> response = new BaseResponseDataModel<TModel>();

            try
            {
                response.Data = repository.GetById(id).Result;

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


        public virtual async Task<BaseResponseDataModel<List<TModel>>> GetAll()
        {
            BaseResponseDataModel<List<TModel>> response = new BaseResponseDataModel<List<TModel>>();

            try
            {
                response.Data = repository.GetAll().ToListAsync().Result;

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



        public virtual async Task<BaseResponseDataModel<List<TModel>>> GetItems([FromBody] BaseSearchRequestModel<TModel> model)
        {
            BaseResponseDataModel<List<TModel>> response = new BaseResponseDataModel<List<TModel>>();

            try
            {
                response = repository.GetItems(model).Result;

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
