using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Common.Database;

/// <summary>
/// Generic repository abstraction over a single aggregate/entity set.
/// Query methods return deferred <see cref="IQueryable{T}"/> (no tracking); write methods stage
/// changes that are persisted by <see cref="SaveChangesAsync"/> (unit of work = DbContext).
/// </summary>
public interface IBaseRepository<TModel>
    where TModel : class, new()
{
    ValueTask<TModel?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    IQueryable<TModel> GetAll();

    IQueryable<TModel> FindBy(Expression<Func<TModel, bool>> predicate);

    /// <summary>Raw SQL query. Interpolated values are sent as DbParameters (SQL injection safe).</summary>
    IQueryable<TModel> FromSql(FormattableString sql);

    Task<TModel?> FirstOrDefaultAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default);

    Task InsertAsync(TModel entity, CancellationToken cancellationToken = default);

    Task InsertAsync(IEnumerable<TModel> entities, CancellationToken cancellationToken = default);

    void Update(TModel entity);

    void Update(IEnumerable<TModel> entities);

    void Delete(TModel entity);

    void Delete(IEnumerable<TModel> entities);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads the tracked entity that has the same primary key as <paramref name="keySource"/> (e.g. a client payload).</summary>
    Task<TModel?> FindByKeyAsync(TModel keySource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mass-assignment safe update: copies only updatable scalar values from <paramref name="source"/> onto the tracked
    /// <paramref name="target"/>. Primary keys, navigations, store generated columns and <c>[Editable(false)]</c> /
    /// <c>[ReadOnly(true)]</c> properties are never taken from the client. Concurrency tokens are used as original values.
    /// </summary>
    void ApplyUpdate(TModel target, TModel source);

    /// <summary>
    /// Mass-assignment safe insert: clears navigation graphs (only the root entity is inserted), resets store generated
    /// keys and <c>[Editable(false, AllowInitialValue = false)]</c> properties.
    /// </summary>
    void PrepareForInsert(TModel entity);

    Task<BaseResponseDataModel<List<TModel>>> GetItemsAsync(BaseSearchRequestModel<TModel> model, CancellationToken cancellationToken = default);

    Task<BaseResponseDataModel<List<TNewModel>>> GetItemsAsync<TNewModel>(
        BaseSearchRequestModel<TNewModel> model,
        IQueryable<TNewModel> query,
        CancellationToken cancellationToken = default)
        where TNewModel : class, new();

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Set based update executed directly in the database (no change tracking).</summary>
    Task<int> ExecuteUpdateAsync(
        Expression<Func<TModel, bool>> predicate,
        Action<UpdateSettersBuilder<TModel>> setters,
        CancellationToken cancellationToken = default);

    /// <summary>Set based delete executed directly in the database (no change tracking).</summary>
    Task<int> ExecuteDeleteAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default);
}
