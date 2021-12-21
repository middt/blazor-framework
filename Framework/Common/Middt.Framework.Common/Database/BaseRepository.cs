using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Database
{
    public abstract class BaseRepository<TModel>
      where TModel : class, new()
    {
        public abstract Task<TModel> GetById(int id);

        public abstract IQueryable<TModel> GetAll();
        public abstract IQueryable<TModel> FindBy(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        public abstract IQueryable<TModel> FromSql(string sql, params object[] parameters);
        public abstract Task<TModel> FirstOrDefault(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        public abstract Task Insert(TModel entity);
        public abstract Task Insert(List<TModel> entityList);
        public abstract Task Update(TModel entity);
        public abstract Task Update(List<TModel> entityList);
        public abstract Task Delete(TModel entity);
        public abstract Task Delete(List<TModel> entityList);
        public abstract Task Save();
        public abstract Task<BaseResponseDataModel<List<TModel>>> GetItems(BaseSearchRequestModel<TModel> model);
        public abstract Task<BaseResponseDataModel<List<TNewModel>>> GetItems<TNewModel>(BaseSearchRequestModel<TNewModel> model, IQueryable<TNewModel> itemList) where TNewModel : class, new();
        public abstract IDbContextTransaction BeginTransaction();
    }
}
