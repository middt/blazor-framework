namespace Middt.Framework.Model.SignalR;

/// <summary>Strongly typed client contract used by <see cref="BaseSignalRHub{TModel}"/>.</summary>
public interface IBaseSignalRClient<TModel>
{
    Task Receive(TModel model);
}
