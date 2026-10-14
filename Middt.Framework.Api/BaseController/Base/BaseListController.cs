using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Api;

public enum CrudOperation
{
    Read,
    Insert,
    Update,
    Delete,
}

/// <summary>
/// Read-only REST endpoints over an <see cref="IBaseRepository{TModel}"/>:
/// <c>GET getbyid?id=</c>, <c>GET getall</c>, <c>POST getitems</c> (filter/sort/page).
/// Errors are reported as ProblemDetails by the global exception handler.
/// </summary>
public abstract class BaseListController<TModel, TRepository>(TRepository repository) : BaseController
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>
{
    protected TRepository Repository { get; } = repository;

    /// <summary>
    /// Row level security for list endpoints (<c>getall</c>, <c>getitems</c>), e.g. <c>query.Where(x =&gt; x.OwnerId == UserId)</c>.
    /// </summary>
    protected virtual IQueryable<TModel> ApplyAccessFilter(IQueryable<TModel> query) => query;

    /// <summary>
    /// Record level authorization (prevents IDOR). Called with the stored entity for read/update/delete and with the
    /// incoming entity for insert. Denied reads/updates/deletes answer 404 so the record's existence is not disclosed.
    /// Protect ownership columns from reassignment with <c>[Editable(false)]</c>.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TModel entity, CrudOperation operation, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    protected ObjectResult RecordNotFound() => Problem(statusCode: StatusCodes.Status404NotFound, title: "Kayıt bulunamadı.");

    [HttpGet("[action]")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<ActionResult<BaseResponseDataModel<TModel>>> GetById(int id, CancellationToken cancellationToken)
    {
        var entity = await Repository.GetByIdAsync(id, cancellationToken);
        return entity is null || !await AuthorizeAsync(entity, CrudOperation.Read, cancellationToken)
            ? RecordNotFound()
            : BaseResponseDataModel<TModel>.Success(entity, 1);
    }

    [HttpGet("[action]")]
    public virtual async Task<ActionResult<BaseResponseDataModel<List<TModel>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await ApplyAccessFilter(Repository.GetAll()).ToListAsync(cancellationToken);
        return BaseResponseDataModel<List<TModel>>.Success(items, items.Count);
    }

    [HttpPost("[action]")]
    [ValidateModel(enableValidation = false)]
    public virtual async Task<ActionResult<BaseResponseDataModel<List<TModel>>>> GetItems(
        [FromBody] BaseSearchRequestModel<TModel> model,
        CancellationToken cancellationToken) =>
        await Repository.GetItemsAsync(model, ApplyAccessFilter(Repository.GetAll()), cancellationToken);
}
