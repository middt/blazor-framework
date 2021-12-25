using System.Collections.Generic;

namespace Middt.Framework.Common.Export
{
    public class BaseExportModel
    {
        public BaseExportModel()
        {
            ParamList = new List<ExportParameterModel>();
        }
        public List<ExportParameterModel> ParamList { get; set; }
    }
    public class ExportParameterModel
    {
        public string Key { get; set; }

        public object Value { get; set; }
    }

}
