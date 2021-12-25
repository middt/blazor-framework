using Middt.Framework.Common.Log;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.RabbitMQ
{
    public abstract class MiddtRabbitMqConnection : IDisposable
    {
        protected string VirtualHost { get; } = "/";

        protected abstract string Exchange { get; }
        protected abstract string Queue { get; }
        protected abstract string AppId { get; }
        protected abstract string QueueAndExchangeRoutingKey { get; }

        protected IModel Channel { get; private set; }
        private IConnection _connection;
        private readonly ConnectionFactory _connectionFactory;
        private readonly ExchangeTypes _exchangeType;
        private readonly bool _isPublisher;
        protected IBaseLog _baselog;

        protected MiddtRabbitMqConnection(IBaseLog baselog,ConnectionFactory connectionFactory, ExchangeTypes exchangeType, bool isPublisher)
        {
            _baselog = baselog;
            _connectionFactory = connectionFactory;
            this._exchangeType = exchangeType;
            this._isPublisher = isPublisher;
            ConnectToRabbitMq();
        }

        protected internal string RefinedExchangeName => $"{VirtualHost}.{Exchange}";
        protected internal string RefinedQueueName => $"{VirtualHost}.{Queue}";
        protected internal string RefinedRoutingKey => $"{VirtualHost}.{QueueAndExchangeRoutingKey}";
        private void ConnectToRabbitMq()
        {
            if (_connection == null || _connection.IsOpen == false)
            {
                _connection = _connectionFactory.CreateConnection();
            }

            if (Channel == null || Channel.IsOpen == false)
            {
                Channel = _connection.CreateModel();
                Channel.ExchangeDeclare(exchange: RefinedExchangeName, type: _exchangeType.ToString(), durable: true, autoDelete: false);
                if (!_isPublisher)
                {
                    Channel.QueueDeclare(queue: RefinedQueueName, durable: true, exclusive: false, autoDelete: false);
                    Channel.QueueBind(queue: RefinedQueueName, exchange: RefinedExchangeName, routingKey: RefinedRoutingKey);
                }
            }
        }

        public void Dispose()
        {
            Channel?.Close();
            Channel?.Dispose();
            Channel = null;

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }
}