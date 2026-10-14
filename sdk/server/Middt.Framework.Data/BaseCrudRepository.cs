using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Common.Database;

/// <summary>
/// EF Core implementation of <see cref="IBaseRepository{TModel}"/>.
/// The <typeparamref name="TContext"/> instance is supplied by dependency injection
/// (register it with <c>AddDbContext</c>/<c>AddDbContextPool</c>).
/// </summary>
public class BaseCrudRepository<TModel, TContext>(TContext context) : IBaseRepository<TModel>
    where TModel : class, new()
    where TContext : DbContext
{
    protected TContext Context { get; } = context;

    protected DbSet<TModel> Entities { get; } = context.Set<TModel>();

    /// <summary>
    /// Order used by <c>GetItemsAsync</c> when the client sends no (valid) sort property. <c>null</c> (default) = the primary key
    /// while paging. Set it for keyless entities (views): without an ORDER BY their pages are not deterministic.
    /// </summary>
    protected virtual string? DefaultSortProperty => null;

    /// <summary>Sort direction of <see cref="DefaultSortProperty"/>.</summary>
    protected virtual bool DefaultSortDescending => false;

    public virtual ValueTask<TModel?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        Entities.FindAsync([id], cancellationToken);

    public virtual IQueryable<TModel> GetAll() => Entities.AsNoTracking();

    public virtual IQueryable<TModel> FindBy(Expression<Func<TModel, bool>> predicate) =>
        Entities.AsNoTracking().Where(predicate);

    public virtual IQueryable<TModel> FromSql(FormattableString sql) =>
        Entities.FromSql(sql).AsNoTracking();

    public virtual Task<TModel?> FirstOrDefaultAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default) =>
        Entities.AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);

    public virtual Task<bool> AnyAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default) =>
        Entities.AnyAsync(predicate, cancellationToken);

    public virtual async Task InsertAsync(TModel entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Entities.AddAsync(entity, cancellationToken);
    }

    public virtual Task InsertAsync(IEnumerable<TModel> entities, CancellationToken cancellationToken = default) =>
        Entities.AddRangeAsync(entities, cancellationToken);

    public virtual void Update(TModel entity) => Entities.Update(entity);

    public virtual void Update(IEnumerable<TModel> entities) => Entities.UpdateRange(entities);

    public virtual void Delete(TModel entity)
    {
        // Entities arriving from the client are detached; attach before removal.
        if (Context.Entry(entity).State == EntityState.Detached)
        {
            Entities.Attach(entity);
        }

        Entities.Remove(entity);
    }

    public virtual void Delete(IEnumerable<TModel> entities)
    {
        foreach (var entity in entities)
        {
            Delete(entity);
        }
    }

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Context.SaveChangesAsync(cancellationToken);

    public virtual async Task<TModel?> FindByKeyAsync(TModel keySource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keySource);

        var key = EntityType.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        var values = key.Properties.Select(p => p.PropertyInfo?.GetValue(keySource)).ToArray();
        return values.Any(v => v is null) ? null : await Entities.FindAsync(values, cancellationToken);
    }

    public virtual void ApplyUpdate(TModel target, TModel source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var entry = Context.Entry(target);
        foreach (var property in EntityType.GetProperties())
        {
            if (property.PropertyInfo is not { } info || property.IsPrimaryKey())
            {
                continue;
            }

            var incoming = info.GetValue(source);

            if (property.IsConcurrencyToken)
            {
                // Optimistic concurrency: compare against the version the client edited.
                entry.Property(property.Name).OriginalValue = incoming;
                continue;
            }

            if (property.GetAfterSaveBehavior() != PropertySaveBehavior.Save || !IsClientEditable(info, isInsert: false))
            {
                continue;
            }

            entry.Property(property.Name).CurrentValue = incoming;
        }
    }

    public virtual void PrepareForInsert(TModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        foreach (var navigation in EntityType.GetNavigations().Cast<INavigationBase>().Concat(EntityType.GetSkipNavigations()))
        {
            if (!navigation.TargetEntityType.IsOwned() && navigation.PropertyInfo is { CanWrite: true } info)
            {
                info.SetValue(entity, null);
            }
        }

        foreach (var property in EntityType.GetProperties())
        {
            if (property.PropertyInfo is not { CanWrite: true } info)
            {
                continue;
            }

            var storeGeneratedKey = property.IsPrimaryKey() && property.ValueGenerated != ValueGenerated.Never;
            if (storeGeneratedKey || !IsClientEditable(info, isInsert: true))
            {
                info.SetValue(entity, info.PropertyType.IsValueType ? Activator.CreateInstance(info.PropertyType) : null);
            }
        }
    }

    private IEntityType EntityType =>
        Context.Model.FindEntityType(typeof(TModel))
        ?? throw new InvalidOperationException($"{typeof(TModel).Name} is not part of {typeof(TContext).Name}.");

    private static bool IsClientEditable(PropertyInfo property, bool isInsert)
    {
        if (property.GetCustomAttribute<ReadOnlyAttribute>() is { IsReadOnly: true })
        {
            return false;
        }

        return property.GetCustomAttribute<EditableAttribute>() is not { } editable
            || editable.AllowEdit
            || (isInsert && editable.AllowInitialValue);
    }

    public virtual Task<BaseResponseDataModel<List<TModel>>> GetItemsAsync(
        BaseSearchRequestModel<TModel> model,
        CancellationToken cancellationToken = default) =>
        GetItemsAsync(model, Entities.AsNoTracking(), cancellationToken);

    public virtual async Task<BaseResponseDataModel<List<TNewModel>>> GetItemsAsync<TNewModel>(
        BaseSearchRequestModel<TNewModel> model,
        IQueryable<TNewModel> query,
        CancellationToken cancellationToken = default)
        where TNewModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);

        query = QueryFilterBuilder.ApplyFilter(query, model.RequestModel);
        query = ApplySort(query, model);

        var count = await query.CountAsync(cancellationToken);

        if (model.RequestItemSize > 0)
        {
            var page = Math.Max(model.CurrentPage, 1);
            query = query.Skip((page - 1) * model.RequestItemSize).Take(model.RequestItemSize);
        }

        var data = await query.ToListAsync(cancellationToken);
        return BaseResponseDataModel<List<TNewModel>>.Success(data, count);
    }

    public virtual Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Context.Database.BeginTransactionAsync(cancellationToken);

    public virtual Task<int> ExecuteUpdateAsync(
        Expression<Func<TModel, bool>> predicate,
        Action<UpdateSettersBuilder<TModel>> setters,
        CancellationToken cancellationToken = default) =>
        Entities.Where(predicate).ExecuteUpdateAsync(setters, cancellationToken);

    public virtual Task<int> ExecuteDeleteAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default) =>
        Entities.Where(predicate).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<TNewModel> ApplySort<TNewModel>(IQueryable<TNewModel> query, BaseSearchRequestModel<TNewModel> model)
        where TNewModel : class, new()
    {
        var entityType = Context.Model.FindEntityType(typeof(TNewModel));

        var sortProperty = QueryFilterBuilder.ResolveSortProperty<TNewModel>(model.SortProperty);

        // Computed / [NotMapped] properties cannot be translated to SQL; treat them like unknown names.
        if (sortProperty is not null && entityType is not null && entityType.FindProperty(sortProperty) is null)
        {
            sortProperty = null;
        }

        if (sortProperty is not null)
        {
            return query.OrderBy($"{sortProperty} {(model.SortReverse ? "ascending" : "descending")}");
        }

        if (QueryFilterBuilder.ResolveSortProperty<TNewModel>(DefaultSortProperty) is { } defaultSort
            && (entityType is null || entityType.FindProperty(defaultSort) is not null))
        {
            return query.OrderBy($"{defaultSort} {(DefaultSortDescending ? "descending" : "ascending")}");
        }

        // Paging without an ORDER BY is non-deterministic; fall back to the primary key when one exists.
        var primaryKey = entityType?.FindPrimaryKey();
        if (model.RequestItemSize > 0 && primaryKey is not null)
        {
            return query.OrderBy(string.Join(", ", primaryKey.Properties.Select(p => p.Name)));
        }

        return query;
    }
}
