
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using System.Text.Json.Serialization;

namespace Suges.Framework.Model.Model.Configuration
{
    public class DependencyModel
    {
        public string ServiceType { get; set; }

        public string ImplementationType { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServiceLifetime Lifetime { get; set; }
    }
}
