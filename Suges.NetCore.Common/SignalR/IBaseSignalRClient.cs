using System.Threading.Tasks;

namespace Suges.Framework.Common.SignalR
{
    public interface IBaseSignalRClient<TModel>
    {
        Task Receive(TModel model);
    }
}
