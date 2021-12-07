using Middt.Framework.Common.Export;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Sample.Common.Export
{
    public class OrderExcelExport : BaseExcelExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "ExcelExport.xlsx";
    }
}
