using Microsoft.AspNetCore.Authorization;
using Middt.Framework.Common.Database;

namespace Middt.Framework.Api;

/// <summary>CRUD controller whose every endpoint requires an authenticated user.</summary>
[Authorize]
public abstract class BaseCrudControllerWithAuth<TModel, TRepository>(TRepository repository)
    : BaseCrudController<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>;

/// <summary>
/// CRUD controller without an authorization requirement. Individual actions can still opt in with <c>[Authorize]</c>.
/// </summary>
public abstract class BaseCrudControllerWithoutAuth<TModel, TRepository>(TRepository repository)
    : BaseCrudController<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>;

/// <summary>Read-only controller whose every endpoint requires an authenticated user.</summary>
[Authorize]
public abstract class BaseListControllerWithAuth<TModel, TRepository>(TRepository repository)
    : BaseListController<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>;

/// <summary>
/// Read-only controller without an authorization requirement. Individual actions can still opt in with <c>[Authorize]</c>.
/// </summary>
public abstract class BaseListControllerWithoutAuth<TModel, TRepository>(TRepository repository)
    : BaseListController<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>;
