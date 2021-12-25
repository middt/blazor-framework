using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.RabbitMQ
{

    public abstract class MiddRabbitMqProducerBase<T> : MiddtRabbitMqConnection
    {

        protected MiddRabbitMqProducerBase(IBaseLog baselog, ConnectionFactory connectionFactory, ExchangeTypes exchangeType)
            : base(baselog,connectionFactory, exchangeType, true) { }

        public BaseResponseModel Publish(T @event)
        {
            BaseResponseModel result = new BaseResponseModel();
            try
            {
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@event));

                var properties = Channel.CreateBasicProperties();
                properties.AppId = AppId;
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2; //persist mode
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                Channel.BasicPublish(exchange: RefinedExchangeName, routingKey: RefinedRoutingKey, basicProperties: properties, body: body);

                result.Result = Model.Model.Enumerations.ResultEnum.Success;
            }
            catch (Exception ex)
            {
                _baselog.Error($"Error occurred in Publish. Message: {ex?.Message}");

                result.Result = Model.Model.Enumerations.ResultEnum.Error;
                result.MessageList.Add(ex.ToString());
            }
            return result;
        }
    }
}