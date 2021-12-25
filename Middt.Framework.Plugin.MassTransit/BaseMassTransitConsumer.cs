using MassTransit;
using MassTransit.RabbitMqTransport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.MassTransit
{
    public abstract class BaseMassTransitConsumer
    {
        public abstract string QueueName { get; }

        protected BaseMassTransitConnection baseMassTransitConnection;
        public BaseMassTransitConsumer(BaseMassTransitConnection _baseMassTransitConnection)
        {
            baseMassTransitConnection = _baseMassTransitConnection;
        }
        public virtual void Create(Action<IReceiveEndpointConfigurator> registrationAction = null)
        {
            var handle = baseMassTransitConnection.GetBus().ConnectReceiveEndpoint(QueueName, x =>
            {
                registrationAction?.Invoke(x);
            });
        }
    }
}
