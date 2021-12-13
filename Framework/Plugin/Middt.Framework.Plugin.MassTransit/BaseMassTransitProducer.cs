using MassTransit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.MassTransit
{
    public  abstract class BaseMassTransitProducer<TCommand>
        where TCommand : class
    {
        public abstract string QueueName { get; }       
        BaseMassTransitConnection baseMassTransitConnection;
        ISendEndpoint endPoint;

        public BaseMassTransitProducer(BaseMassTransitConnection _baseMassTransitConnection)
        {
            baseMassTransitConnection = _baseMassTransitConnection;
        }
        private void Create()
        {            
            Uri sendToUri = new Uri($"{baseMassTransitConnection.GetBus().Address.AbsoluteUri}/{QueueName}");
            endPoint = baseMassTransitConnection.GetBus().GetSendEndpoint(sendToUri).Result;            
        }
        public void Send(TCommand model)
        {
            endPoint.Send<TCommand>(model);
        }
    }
}
