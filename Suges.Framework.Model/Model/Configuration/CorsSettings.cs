using Suges.Framework.Model.Model.Configuration;
using System.Collections.Generic;

namespace Suges.Framework.Api.Configuration.Model
{
    public class CorsSettings
    {
        public CorsSettings()
        {
            CorsModelList = new List<CorsModel>();
        }
        public List<CorsModel> CorsModelList { get; set; }
    }
}
