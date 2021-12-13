using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.MassTransit
{
    public  class BaseMassTransitConsumer
    {
        //public abstract string QueueName { get; }

        protected BaseMassTransitConnection baseMassTransitConnection;
        public BaseMassTransitConsumer(BaseMassTransitConnection _baseMassTransitConnection)
        {
            baseMassTransitConnection = _baseMassTransitConnection;
        }
        public virtual void Create()
        {
            //var handle = baseMassTransitConnection.GetBus().ConnectReceiveEndpoint("secondary-queue", x =>
            //{
            //    x.Consumer<SomeConsumer>();
            //});
        }
    }
}
