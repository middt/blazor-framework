using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Api;

/// <summary>
/// Adds <c>POST insert</c>, <c>POST update</c> and <c>POST delete</c> to <see cref="BaseListController{TModel,TRepository}"/>.
/// Writes are mass-assignment safe: updates load the stored record and copy only editable scalar properties
/// (<see cref="IBaseRepository{TModel}.ApplyUpdate"/>), inserts drop navigation graphs and client chosen identity values.
/// </summary>
public abstract class BaseCrudController<TModel, TRepository>(TRepository repository)
    : BaseListController<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>
{
    [HttpPost("[action]")]
    [ValidateModel]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<ActionResult<BaseResponseDataModel<TModel>>> Insert([FromBody] TModel model, CancellationToken cancellationToken)
    {
        Repository.PrepareForInsert(model);
        if (!await AuthorizeAsync(model, CrudOperation.Insert, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bu işlem için yetkiniz yok.");
        }

        await Repository.InsertAsync(model, cancellationToken);
        await Repository.SaveChangesAsync(cancellationToken);
        return BaseResponseDataModel<TModel>.Success(model, 1);
    }

    [HttpPost("[action]")]
    [ValidateModel]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public virtual async Task<ActionResult<BaseResponseDataModel<TModel>>> Update([FromBody] TModel model, CancellationToken cancellationToken)
    {
        var existing = await Repository.FindByKeyAsync(model, cancellationToken);
        if (existing is null || !await AuthorizeAsync(existing, CrudOperation.Update, cancellationToken))
        {
            return RecordNotFound();
        }

        Repository.ApplyUpdate(existing, model);
        await Repository.SaveChangesAsync(cancellationToken);
        return BaseResponseDataModel<TModel>.Success(existing, 1);
    }

    [HttpPost("[action]")]
    [ValidateModel(enableValidation = false)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public virtual async Task<ActionResult<BaseResponseDataModel<TModel>>> Delete([FromBody] TModel model, CancellationToken cancellationToken)
    {
        // Only the key of the payload is used; the stored record is what gets authorized and deleted.
        var existing = await Repository.FindByKeyAsync(model, cancellationToken);
        if (existing is null || !await AuthorizeAsync(existing, CrudOperation.Delete, cancellationToken))
        {
            return RecordNotFound();
        }

        Repository.Delete(existing);
        await Repository.SaveChangesAsync(cancellationToken);
        return BaseResponseDataModel<TModel>.Success(existing, 1);
    }
}
