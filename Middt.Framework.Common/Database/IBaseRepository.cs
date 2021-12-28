using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Database
{
    public interface IBaseRepository<TModel>
      where TModel : class, new()
    {
        Task<TModel> GetById(int id);
        IQueryable<TModel> GetAll();
        IQueryable<TModel> FindBy(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        IQueryable<TModel> FromSql(string sql, params object[] parameters);
        Task<TModel> FirstOrDefault(System.Linq.Expressions.Expression<Func<TModel, bool>> predicate);
        Task Insert(TModel entity);
        Task Insert(List<TModel> entityList);
        Task Update(TModel entity);
        Task Update(List<TModel> entityList);
        Task Delete(TModel entity);
        Task Delete(List<TModel> entityList);
        Task Save();
        Task<BaseResponseDataModel<List<TModel>>> GetItems(BaseSearchRequestModel<TModel> model);
        Task<BaseResponseDataModel<List<TNewModel>>> GetItems<TNewModel>(BaseSearchRequestModel<TNewModel> model, IQueryable<TNewModel> itemList) where TNewModel : class, new();
        Task<IDbContextTransaction> BeginTransaction();

        Task BulkInsert(List<TModel> entityList);
        Task BulkInsert(List<TModel> entityList, params string[] propertiesToExclude);

        Task BulkUpdate(List<TModel> entityList);

        Task<int> BulkUpdate(Expression<Func<TModel, bool>> queryExpression, Expression<Func<TModel, TModel>> updateExpression);

        Task BulkDelete(List<TModel> entityList);

        Task<int> BulkDelete(Expression<Func<TModel, bool>> queryExpression);
    }
}
