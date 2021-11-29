using Middt.Framework.Common.Export;

namespace Middt.Sample.Common.Service
{
    public class Table1ExcelToPdfExport : BaseExcelToPdfExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokum.xlsx";
    }
}
