using Middt.Framework.Common.Export;

namespace Middt.Template.Common.Service
{
    public class Table1ExcelExport : BaseExcelExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokum.xlsx";
    }
}
