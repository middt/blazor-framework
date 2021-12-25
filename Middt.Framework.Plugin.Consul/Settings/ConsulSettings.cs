using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Consul.Settings
{
    public class ConsulSettings
    {
        public string ServiceName { get; set; }
        public string ConsoleHost { get; set; }
        public string HealthyCheckURL { get; set; }
    }
}
