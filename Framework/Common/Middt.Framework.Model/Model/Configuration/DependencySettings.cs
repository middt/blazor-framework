using System.Collections.Generic;

namespace Middt.Framework.Model.Model.Configuration
{
    public class DependencySettings
    {
        public DependencySettings()
        {
            DependencyModelList = new List<DependencyModel>();
        }
        public List<DependencyModel> DependencyModelList { get; set; }
    }
}
