using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Helper
{
    public class JsonHelper<T>
    {
        JsonSerializerSettings settings;
        public JsonHelper()
        {
            settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
        }

        public T DeserializeObject(string value)
        {
            return JsonConvert.DeserializeObject<T>(value, settings);
        }

        public string SerializeObject(T value)
        {
            return JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.None, settings);
        }
    }
}
