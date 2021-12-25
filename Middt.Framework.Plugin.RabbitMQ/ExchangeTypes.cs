using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.RabbitMQ
{
    public enum ExchangeTypes
    {
        Direct,
        Fanout,
        Topic,
        Headers
    }
}
