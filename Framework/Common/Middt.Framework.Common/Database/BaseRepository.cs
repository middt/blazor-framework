using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Middt.Framework.Common.Database
{
    public abstract class BaseRepository<TModel>
      where TModel : class, new()
    {
        public abstract TModel GetById(int id);

        public abstract IQueryable<TModel> GetAll();
        public abstract IQueryable<TModel> FindBy(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        public abstract IQueryable<TModel> FromSql(string sql, params object[] parameters);
        public abstract TModel FirstOrDefault(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        public abstract void Insert(TModel entity);
        public abstract void Insert(List<TModel> entityList);
        public abstract void Update(TModel entity);
        public abstract void Update(List<TModel> entityList);
        public abstract void Delete(TModel entity);
        public abstract void Delete(List<TModel> entityList);
        public abstract void Save();
        public abstract BaseResponseDataModel<List<TModel>> GetItems(BaseSearchRequestModel<TModel> model);
        public abstract BaseResponseDataModel<List<TNewModel>> GetItems<TNewModel>(BaseSearchRequestModel<TNewModel> model, IQueryable<TNewModel> itemList) where TNewModel : class, new();
        public abstract IDbContextTransaction BeginTransaction();
    }
}
