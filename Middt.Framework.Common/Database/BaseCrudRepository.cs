using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Database
{
    public class BaseCrudRepository<TModel, TContext> : IBaseRepository<TModel>
      where TModel : class, new()
      where TContext : DbContext, new()
        // where T : BaseEntity
    {
        protected readonly TContext context;
        private readonly DbSet<TModel> entities;

        public TContext Context
        {
            get
            {
                return context;
            }
        }
        public BaseCrudRepository() : this(new TContext())
        {
        }
        public BaseCrudRepository(TContext _context)
        {
            this.context = _context;
            entities = context.Set<TModel>();


        }

        public async Task<TModel> GetById(int id)
        {
            return await entities.FindAsync(id);
        }

        public IQueryable<TModel> GetAll()
        {
            return entities.AsNoTracking();
        }
        public IQueryable<TModel> FindBy(Expression<Func<TModel, bool>> queryExpression)
        {
            return entities.AsNoTracking().Where(queryExpression);
        }


        public IQueryable<TModel> FromSql(string sql, params object[] parameters)
        {
            return entities.FromSqlRaw(sql, parameters).AsNoTracking();
        }
        public Task<TModel> FirstOrDefault(Expression<Func<TModel, bool>> queryExpression)
        {

            return entities.AsNoTracking().FirstOrDefaultAsync(queryExpression);
        }

        public async Task Insert(TModel entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }
            await entities.AddAsync(entity);
        }
        public async Task Insert(List<TModel> entityList)
        {
            foreach (TModel entity in entityList)
            {
                await entities.AddAsync(entity);
            }
        }


        public async Task Update(TModel entity)

        {
            context.Entry(entity).State = EntityState.Modified;
        }
        public async Task Update(List<TModel> entityList)
        {
            foreach (TModel entity in entityList)
            {
                context.Entry(entity).State = EntityState.Modified;
            }
        }

        public async Task Delete(TModel entity)
        {
            entities.Remove(entity);
        }
        public async Task Delete(List<TModel> entityList)
        {
            foreach (TModel entity in entityList)
            {
                entities.Remove(entity);
            }
        }

        public async Task Save()
        {
            await context.SaveChangesAsync();
        }

        public async Task<BaseResponseDataModel<List<TModel>>> GetItems(BaseSearchRequestModel<TModel> model)
        {
            return await GetItems<TModel>(model, entities.AsNoTracking());
        }

        private async Task<IQueryable<TNewModel>> StringQuery<TNewModel>(PropertyInfo p, IQueryable<TNewModel> itemList, object value)
        {
            QueryStringAttribute[] QueryStringAttributeList = (QueryStringAttribute[])p.GetCustomAttributes(typeof(QueryStringAttribute), true);

            if (QueryStringAttributeList.Length > 0)
            {
                QueryStringAttribute queryStringAttribute = QueryStringAttributeList[0];

                if (queryStringAttribute.SearchType == StringSearchType.Any)
                {
                    itemList = itemList.Where(p.Name + ".Any(@0)", value);
                }
                else if (queryStringAttribute.SearchType == StringSearchType.Contains)
                {
                    itemList = itemList.Where(p.Name + ".Contains(@0)", value);
                }
                else if (queryStringAttribute.SearchType == StringSearchType.EndsWith)
                {
                    itemList = itemList.Where(p.Name + ".EndsWith(@0)", value);
                }
                else if (queryStringAttribute.SearchType == StringSearchType.StartsWith)
                {
                    itemList = itemList.Where(p.Name + ".StartsWith(@0)", value);
                }
            }
            else
            {
                itemList = itemList.Where(p.Name + ".Contains(@0)", value);
            }

            return itemList;
        }
        private async Task<IQueryable<TNewModel>> IntQuery<TNewModel>(PropertyInfo p, IQueryable<TNewModel> itemList, object value)
        {
            var IsZeroSearch = Attribute.IsDefined(p, typeof(QueryIsZeroAttribute));
            if (IsZeroSearch)
            {
                itemList = await IntQueryExtend<TNewModel>(p, itemList, value);
            }
            else
            {
                if (Convert.ToInt64(value) != 0)
                {
                    itemList = await IntQueryExtend<TNewModel>(p, itemList, value);
                }
            }
            return itemList;
        }
        private async  Task<IQueryable<TNewModel>> IntQueryExtend<TNewModel>(PropertyInfo p, IQueryable<TNewModel> itemList, object value)
        {

            QueryIntAttribute[] QueryIntAttributeList = (QueryIntAttribute[])p.GetCustomAttributes(typeof(QueryIntAttribute), true);

            if (QueryIntAttributeList.Length > 0)
            {
                QueryIntAttribute queryIntAttributeList = QueryIntAttributeList[0];

                if (queryIntAttributeList.SearchType == IntSearchType.Equal)
                {
                    itemList = itemList.Where(p.Name + " == @0", value);
                }
                else if (queryIntAttributeList.SearchType == IntSearchType.GreaterThanOrEqual)
                {
                    itemList = itemList.Where(p.Name + " >= @0", value);
                }
                else if (queryIntAttributeList.SearchType == IntSearchType.GreatThan)
                {
                    itemList = itemList.Where(p.Name + " > @0", value);
                }
                else if (queryIntAttributeList.SearchType == IntSearchType.LessThan)
                {
                    itemList = itemList.Where(p.Name + " < @0", value);
                }
                else if (queryIntAttributeList.SearchType == IntSearchType.LessThanOrEqual)
                {
                    itemList = itemList.Where(p.Name + " <= @0", value);
                }
            }
            else
            {
                itemList = itemList.Where(p.Name + " == @0", value);
            }

            return itemList;
        }


        private async Task<IQueryable<TNewModel>> DateQuery<TNewModel>(PropertyInfo p, IQueryable<TNewModel> itemList, object value)
        {

            QueryDateAttribute[] QueryDateAttributeList = (QueryDateAttribute[])p.GetCustomAttributes(typeof(QueryDateAttribute), true);

            if (QueryDateAttributeList.Length > 0)
            {
                QueryDateAttribute queryDateAttribute = QueryDateAttributeList[0];
                string paramName = p.Name;
                if (!string.IsNullOrEmpty(queryDateAttribute.PropertyName))
                {
                    paramName = queryDateAttribute.PropertyName;
                }

                if ((DateTime)value != default(DateTime))
                {
                    if (queryDateAttribute.SearchType == DateSearchType.Equal)
                    {
                        itemList = itemList.Where(paramName + " == @0", value);
                    }
                    else if (queryDateAttribute.SearchType == DateSearchType.GreaterThanOrEqual)
                    {
                        itemList = itemList.Where(paramName + " >= @0", value);
                    }
                    else if (queryDateAttribute.SearchType == DateSearchType.GreatThan)
                    {
                        itemList = itemList.Where(paramName + " > @0", value);
                    }
                    else if (queryDateAttribute.SearchType == DateSearchType.LessThan)
                    {
                        itemList = itemList.Where(paramName + " < @0", value);
                    }
                    else if (queryDateAttribute.SearchType == DateSearchType.LessThanOrEqual)
                    {
                        itemList = itemList.Where(paramName + " <= @0", value);
                    }
                }

            }
            else
            {
                if ( (DateTime)value != default(DateTime))
                {
                    itemList = itemList.Where(p.Name + " == @0", value);
                }
            }

            return itemList;
        }

        //
        public async Task<BaseResponseDataModel<List<TNewModel>>> GetItems<TNewModel>(BaseSearchRequestModel<TNewModel> model, IQueryable<TNewModel> itemList) where TNewModel : class, new()

        {
            var properties = typeof(TNewModel).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (model.RequestModel != null)
            {
                foreach (var p in properties)
                {
                    var IsInFilter = Attribute.IsDefined(p, typeof(QueryIsNoFilterAttribute));

                    if (!IsInFilter)
                    {
                        var value = typeof(TNewModel).GetProperty(p.Name).GetValue(model.RequestModel);
                        if (value != null)
                        {
                            if (value is string)
                            {
                                itemList = await StringQuery<TNewModel>(p, itemList, value);
                            }
                            else if (
                                value is long ||
                                value is Int64 ||
                                value is Int32 ||
                                value is Int16 ||
                                value is UInt64 ||
                                value is UInt32 ||
                                value is UInt16 ||
                                value is double ||
                                value is short ||
                                value is int ||
                                value is uint ||
                                value is ushort ||
                                value is float)
                            {
                                itemList = await IntQuery<TNewModel>(p, itemList, value);
                            }
                            else if (value is bool)
                            {
                                itemList = itemList.Where(p.Name + " == @0", value);
                            }
                            else if (value is DateTime)
                            {
                                itemList = await DateQuery<TNewModel>(p, itemList, value);
                            }
                        }
                    }
                }
            }
            if (itemList != null)
            {
                if (!string.IsNullOrEmpty(model.SortProperty))
                {
                    if (model.SortReverse)
                    {
                        itemList = itemList.OrderBy(model.SortProperty);
                    }
                    else
                    {
                        Expression resultExpression = null;
                        var parameter = Expression.Parameter(typeof(TNewModel), "p");

                        var property = typeof(TNewModel).GetProperty(model.SortProperty);
                        // this is the part p.SortColumn
                        var propertyAccess = Expression.MakeMemberAccess(parameter, property);

                        // this is the part p =&gt; p.SortColumn
                        var orderByExpression = Expression.Lambda(propertyAccess, parameter);

                        string command = "OrderByDescending";
                        // finally, call the "OrderBy" / "OrderByDescending" method with the order by lamba expression
                        resultExpression = Expression.Call(typeof(Queryable), command, new Type[] { typeof(TNewModel), property.PropertyType },
                           itemList.Expression, Expression.Quote(orderByExpression));

                        itemList = itemList.Provider.CreateQuery<TNewModel>(resultExpression);
                    }
                }
            }
            BaseResponseDataModel<List<TNewModel>> response = new BaseResponseDataModel<List<TNewModel>>();


            response.Count = itemList.Count();

            if (model.RequestItemSize > 0)
                response.Data = await itemList
                            .Skip(((model.CurrentPage) - 1) * model.RequestItemSize)
                            .Take(model.RequestItemSize).ToListAsync();
            else
                response.Data = await itemList.ToListAsync();

            return response;
        }

        public async Task<IDbContextTransaction> BeginTransaction()
        {
            return await Context.Database.BeginTransactionAsync();
        }



        public async Task BulkInsert(List<TModel> entityList)
        {
            await BulkInsert(entityList, new BulkConfig());
        }
        public async Task BulkInsert(List<TModel> entityList, params string[] propertiesToExclude)
        {
            List<string> exlude = new List<string>();
            if (propertiesToExclude != null && propertiesToExclude.Count() > 0)
            {
                exlude = new List<string>(propertiesToExclude);
            }
            BulkConfig bulkConfig = new BulkConfig() { PropertiesToExclude = exlude };
            await BulkInsert(entityList, bulkConfig);
        }


        public async Task BulkInsert(List<TModel> entityList, BulkConfig bulkConfig)
        {
            await context.BulkInsertAsync<TModel>(entityList, bulkConfig);
        }
        public async Task BulkUpdate(List<TModel> entityList)
        {
            await BulkUpdate(entityList, new BulkConfig());
        }


        public async Task BulkUpdate(List<TModel> entityList, BulkConfig bulkConfig)
        {
            await context.BulkUpdateAsync<TModel>(entityList, bulkConfig);
        }
        public async Task<int> BulkUpdate(Expression<Func<TModel, bool>> queryExpression, Expression<Func<TModel, TModel>> updateExpression)
        {
            return await entities.Where(queryExpression).BatchUpdateAsync<TModel>(updateExpression);
        }


        public async Task BulkDelete(List<TModel> entityList)
        {
            await BulkDelete(entityList, new BulkConfig());
        }
        public async Task BulkDelete(List<TModel> entityList, BulkConfig bulkConfig)
        {
            await context.BulkDeleteAsync<TModel>(entityList, bulkConfig);
        }
        public async Task<int> BulkDelete(Expression<Func<TModel, bool>> queryExpression)
        {
            return await entities.Where(queryExpression).BatchDeleteAsync();
        }
    }
}
