using Middt.Framework.Model.Model.Configuration;
using System.Collections.Generic;

namespace Middt.Framework.Api.Configuration.Model
{
    public class CorsModel
    {
        public string Name { get; set; }
        public string[] Url { get; set; }
    }

    public class CorsSettings
    {
        public CorsSettings()
        {
            CorsModelList = new List<CorsModel>();
        }
        public List<CorsModel> CorsModelList { get; set; }
    }
}
