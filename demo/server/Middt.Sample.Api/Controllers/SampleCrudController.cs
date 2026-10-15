using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Api;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Controllers;

/// <summary>
/// Base of the sample's CRUD controllers: every signed-in user may read, only the <c>manager</c> role may create, change
/// or delete (record level check through <see cref="BaseListController{TModel,TRepository}.AuthorizeAsync"/>; denied
/// writes answer 404 like a missing record). Real applications add ownership/tenant rules here or in ApplyAccessFilter.
/// <para>
/// Records with a lifecycle workflow (<see cref="ChangedThroughWorkflows"/>) are never written here: insert, update and
/// delete answer 403 so every change passes the workflow's approval. Their <c>getall</c> (lookups: selects of forms and
/// filters) returns only active records, <c>getitems</c> (lists) returns all and filters by <see cref="ILifecycleRecord.Status"/>.
/// </para>
/// </summary>
public abstract class SampleCrudController<TModel, TRepository>(TRepository repository)
    : BaseCrudControllerWithAuth<TModel, TRepository>(repository)
    where TModel : class, new()
    where TRepository : IBaseRepository<TModel>
{
    public const string WriterRole = "manager";

    /// <summary>ProblemDetails detail of a refused write on a workflow managed record.</summary>
    public const string WorkflowOnlyDetail = "Bu kayıt iş akışı üzerinden değiştirilir.";

    private static readonly MiddtApiOptions DefaultApiOptions = new();

    /// <summary>The records change only through workflows: the write endpoints are closed.</summary>
    protected virtual bool ChangedThroughWorkflows => false;

    /// <summary>Selects the active records of an entity with an active flag (<c>null</c>: the entity has none).</summary>
    protected virtual Expression<Func<TModel, bool>>? ActiveRecords => null;

    private MiddtApiOptions ApiOptions => HttpContext.RequestServices.GetService<MiddtApiOptions>() ?? DefaultApiOptions;

    protected override Task<bool> AuthorizeAsync(TModel entity, CrudOperation operation, CancellationToken cancellationToken) =>
        Task.FromResult(operation == CrudOperation.Read || User.IsInRole(WriterRole));

    [HttpGet("[action]")]
    public override async Task<ActionResult<BaseResponseDataModel<List<TModel>>>> GetAll(CancellationToken cancellationToken)
    {
        if (ActiveRecords is not { } active)
        {
            return await base.GetAll(cancellationToken);
        }

        // Lookups offer active records only; the repository's order (newest first for customers/products) is kept.
        var items = await ApplyAccessFilter(Repository.GetAll()).Where(active).Take(ApiOptions.MaxGetAllItems).ToListAsync(cancellationToken);
        return BaseResponseDataModel<List<TModel>>.Success(items, items.Count);
    }

    [HttpPost("[action]")]
    [ValidateModel(enableValidation = false)]
    public override async Task<ActionResult<BaseResponseDataModel<List<TModel>>>> GetItems(
        [FromBody] BaseSearchRequestModel<TModel> model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ActiveRecords is not { } active || model.RequestModel is not ILifecycleRecord { Status: { } status } ||
            status is not (ILifecycleRecord.ActiveText or ILifecycleRecord.PassiveText))
        {
            return await base.GetItems(model, cancellationToken);
        }

        var maxPageSize = ApiOptions.MaxPageSize;
        model.RequestItemSize = model.RequestItemSize <= 0 ? maxPageSize : Math.Min(model.RequestItemSize, maxPageSize);
        var filter = status == ILifecycleRecord.ActiveText
            ? active
            : Expression.Lambda<Func<TModel, bool>>(Expression.Not(active.Body), active.Parameters);
        return await Repository.GetItemsAsync(model, ApplyAccessFilter(Repository.GetAll()).Where(filter), cancellationToken);
    }

    public override Task<ActionResult<BaseResponseDataModel<TModel>>> Insert([FromBody] TModel model, CancellationToken cancellationToken) =>
        ChangedThroughWorkflows ? Task.FromResult(WorkflowOnly()) : base.Insert(model, cancellationToken);

    public override Task<ActionResult<BaseResponseDataModel<TModel>>> Update([FromBody] TModel model, CancellationToken cancellationToken) =>
        ChangedThroughWorkflows ? Task.FromResult(WorkflowOnly()) : base.Update(model, cancellationToken);

    public override Task<ActionResult<BaseResponseDataModel<TModel>>> Delete([FromBody] TModel model, CancellationToken cancellationToken) =>
        ChangedThroughWorkflows ? Task.FromResult(WorkflowOnly()) : base.Delete(model, cancellationToken);

    private ActionResult<BaseResponseDataModel<TModel>> WorkflowOnly() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Doğrudan değişiklik kapalı.", detail: WorkflowOnlyDetail);
}
