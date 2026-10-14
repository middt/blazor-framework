using Middt.Framework.Common.SignalR.Model;

namespace Middt.Framework.Common.SignalR;

public interface IBaseSignalRHub<TModel>
{
    Task Send(BaseSignalRRequestModel<TModel> sendModel);
}
