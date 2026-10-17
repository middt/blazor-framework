using Middt.Framework.Common.Database;

namespace Middt.Framework.Api;

/// <summary>
/// CRUD base for application data that requires an explicit list filter and record policy.
/// Use this for tenant or owner scoped entities so a new controller cannot silently inherit permissive defaults.
/// </summary>
public abstract class BasePolicyCrudController<TModel, TRepository>(TRepository repository)
    : BaseCrudControllerWithAuth<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>
{
    /// <summary>Restricts every list query to records visible to the caller.</summary>
    protected abstract override IQueryable<TModel> ApplyAccessFilter(IQueryable<TModel> query);

    /// <summary>Authorizes a single stored record or a proposed insert.</summary>
    protected abstract override Task<bool> AuthorizeAsync(TModel entity, CrudOperation operation, CancellationToken cancellationToken);
}
