using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Common.Http;

/// <summary>Read operations exposed by <c>BaseListController</c> endpoints.</summary>
public interface IListApiClient<TModel>
    where TModel : new()
{
    Task<BaseResponseDataModel<TModel>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<BaseResponseDataModel<List<TModel>>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<BaseResponseDataModel<List<TModel>>> GetItemsAsync(BaseSearchRequestModel<TModel> model, CancellationToken cancellationToken = default);
}

/// <summary>Read/write operations exposed by <c>BaseCrudController</c> endpoints.</summary>
public interface ICrudApiClient<TModel> : IListApiClient<TModel>
    where TModel : new()
{
    Task<BaseResponseDataModel<TModel>> InsertAsync(TModel model, CancellationToken cancellationToken = default);

    Task<BaseResponseDataModel<TModel>> UpdateAsync(TModel model, CancellationToken cancellationToken = default);

    Task<BaseResponseDataModel<TModel>> DeleteAsync(TModel model, CancellationToken cancellationToken = default);
}
