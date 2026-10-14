using Middt.Framework.Api;
using Middt.Framework.Common.Database;

namespace Middt.Sample.Api.Controllers;

/// <summary>
/// Base of the sample's CRUD controllers: every signed-in user may read, only the <c>manager</c> role may create, change
/// or delete (record level check through <see cref="BaseListController{TModel,TRepository}.AuthorizeAsync"/>; denied
/// writes answer 404 like a missing record). Real applications add ownership/tenant rules here or in ApplyAccessFilter.
/// </summary>
public abstract class SampleCrudController<TModel, TRepository>(TRepository repository)
    : BaseCrudControllerWithAuth<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>
{
    public const string WriterRole = "manager";

    protected override Task<bool> AuthorizeAsync(TModel entity, CrudOperation operation, CancellationToken cancellationToken) =>
        Task.FromResult(operation == CrudOperation.Read || User.IsInRole(WriterRole));
}
