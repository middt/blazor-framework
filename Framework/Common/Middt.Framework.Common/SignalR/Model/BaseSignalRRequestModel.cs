using System.Collections.Generic;

namespace Middt.Framework.Common.SignalR.Model
{
    public enum MessageType
    {
        Broadcast = 0,
        Group = 1,
        User = 2
    }
    public class BaseSignalRRequestModel<TModel>
    {
        public BaseSignalRRequestModel()
        {
            MessageType = MessageType.Broadcast;
            MessageTo = new List<string>();
        }
        public MessageType MessageType { get; set; }

        public List<string> MessageTo { get; set; }

        public TModel Data { get; set; }
    }
}
