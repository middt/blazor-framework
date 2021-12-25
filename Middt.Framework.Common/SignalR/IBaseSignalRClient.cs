using System.Threading.Tasks;

namespace Middt.Framework.Common.SignalR
{
    public interface IBaseSignalRClient<TModel>
    {
        Task Receive(TModel model);
    }
}
