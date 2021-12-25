using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Middt.Framework.Api
{
    public abstract class BaseCrudController<TModel, TRepository> : BaseListController<TModel, TRepository>
        where TModel : class, new()
        where TRepository : BaseRepository<TModel>, new()
    {

        protected TRepository repository;
        protected BaseCrudController()
        {
            repository = new TRepository();
        }

        public virtual async Task<BaseResponseDataModel<TModel>> Insert([FromBody] TModel model)
        {
            BaseResponseDataModel<TModel> response = new BaseResponseDataModel<TModel>();
            try
            {
                if (!ModelState.IsValid)
                {
                    List<ModelStateEntry> modelStateEntryList = ModelState.Values.ToList();
                    if (modelStateEntryList != null && modelStateEntryList.Count > 0)
                    {
                        List<ModelError> modelErrorList = null;
                        foreach (ModelStateEntry modelStateEntry in modelStateEntryList)
                        {
                            modelErrorList = modelStateEntry.Errors.ToList();

                            foreach (ModelError modelError in modelErrorList)
                            {
                                response.MessageList.Add(modelError.ErrorMessage);
                            }
                        }
                    }
                    response.Result = ResultEnum.Error;
                    return response;
                }



                await repository.Insert(model);
                await repository.Save();

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");
                response.Data = model;

            }
            catch (Exception ex)
            {
                response.Result = ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }
        public virtual async Task<BaseResponseDataModel<TModel>> Update(TModel model)
        {
            BaseResponseDataModel<TModel> response = new BaseResponseDataModel<TModel>();
            try
            {
                if (!ModelState.IsValid)
                {
                    List<ModelStateEntry> modelStateEntryList = ModelState.Values.ToList();
                    if (modelStateEntryList != null && modelStateEntryList.Count > 0)
                    {
                        List<ModelError> modelErrorList = null;
                        foreach (ModelStateEntry modelStateEntry in modelStateEntryList)
                        {
                            modelErrorList = modelStateEntry.Errors.ToList();

                            foreach (ModelError modelError in modelErrorList)
                            {
                                response.MessageList.Add(modelError.ErrorMessage);
                            }
                        }
                    }
                    response.Result = ResultEnum.Error;
                    return response;
                }

                await repository.Update(model);
                await repository.Save();

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");
                response.Data = model;

            }
            catch (Exception ex)
            {
                response.Result = ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }
        public virtual async Task<BaseResponseDataModel<TModel>> Delete(TModel model)
        {
            BaseResponseDataModel<TModel> response = new BaseResponseDataModel<TModel>();
            try
            {
                await repository.Delete(model);
                await repository.Save();

                response.Result = ResultEnum.Success;
                response.MessageList.Add("Ok");
                response.Data = model;

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
