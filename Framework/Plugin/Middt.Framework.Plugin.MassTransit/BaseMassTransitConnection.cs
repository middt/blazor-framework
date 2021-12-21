using MassTransit;
using MassTransit.RabbitMqTransport;

using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Plugin.MassTransit.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.MassTransit
{
    public class BaseMassTransitConnection
    {

        IBaseConfiguration baseConfiguration;
        IBusControl bus;

        public IBusControl GetBus()
        {
            return bus;
        }
        public BaseMassTransitConnection(IBaseConfiguration _baseConfiguration)
        {
            baseConfiguration = _baseConfiguration;
            Create();
        }

        private void Create()
        {
            RabbitMqSettings rabbitMQSettings = baseConfiguration.Get<RabbitMqSettings>();


            bus = Bus.Factory.CreateUsingRabbitMq(factory =>
            {
                factory.Host(rabbitMQSettings.URL, configurator =>
                {
                    configurator.Username(rabbitMQSettings.Username);
                    configurator.Password(rabbitMQSettings.Password);
                });
            });
        }
    }
}
