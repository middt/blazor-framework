namespace Middt.Framework.Common.SignalR.Model;

public enum MessageType
{
    Broadcast = 0,
    Group = 1,
    User = 2,
}

public class BaseSignalRRequestModel<TModel>
{
    public MessageType MessageType { get; set; } = MessageType.Broadcast;

    public List<string> MessageTo { get; set; } = [];

    public TModel? Data { get; set; }
}
