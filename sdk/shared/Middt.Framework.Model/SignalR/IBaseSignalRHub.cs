namespace Middt.Framework.Model.SignalR;

public interface IBaseSignalRHub<TModel>
{
    Task Send(BaseSignalRRequestModel<TModel> sendModel);
}
