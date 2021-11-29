using Suges.Framework.Common.SignalR.Model;
using System.Threading.Tasks;

namespace Suges.Framework.Common.SignalR
{
    public interface IBaseSignalRHub<TModel>
    {

        Task Send(BaseSignalRRequestModel<TModel> sendModel);

        //void Send(TModel sendModel,string connectionID);

    }
}
