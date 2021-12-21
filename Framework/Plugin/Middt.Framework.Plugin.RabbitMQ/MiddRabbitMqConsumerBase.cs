using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.RabbitMQ
{
    public abstract class MiddRabbitMqConsumerBase<TMessage> : MiddtRabbitMqConnection
    {
        public Action<TMessage> OnReceiveAction { get; set; }

        public MiddRabbitMqConsumerBase(ConnectionFactory connectionFactory, ExchangeTypes exchangeType)
            : base(connectionFactory, exchangeType, false)
        {
        }


        protected void Consume()
        {
            var consumer = new AsyncEventingBasicConsumer(Channel);
            consumer.Received += OnEventReceived;
            Channel.BasicConsume(queue: RefinedQueueName, autoAck: false, consumer: consumer);
        }

        private Task OnEventReceived(object sender, BasicDeliverEventArgs @event)
        {
            try
            {
                var body = Encoding.UTF8.GetString(@event.Body.ToArray());
                var message = JsonConvert.DeserializeObject<TMessage>(body);

                ReceiveAction(message);

                Channel.BasicAck(@event.DeliveryTag, false);

                OnReceiveAction?.Invoke(message);

                return Task.CompletedTask;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred in OnEventReceived. Message: {ex?.Message}");
                Channel.BasicNack(@event.DeliveryTag, false, true);
                throw;
            }
            finally
            {
            }
        }

        protected virtual void ReceiveAction(TMessage message)
        {

        }

    }
}