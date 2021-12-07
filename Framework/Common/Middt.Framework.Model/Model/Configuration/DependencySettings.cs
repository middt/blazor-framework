using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Middt.Framework.Model.Model.Configuration
{
    public class DependencyModel
    {
        public string ServiceType { get; set; }

        public string ImplementationType { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServiceLifetime Lifetime { get; set; }
    }

    public class DependencySettings
    {
        public DependencySettings()
        {
            DependencyModelList = new List<DependencyModel>();
        }
        public List<DependencyModel> DependencyModelList { get; set; }
    }
}
