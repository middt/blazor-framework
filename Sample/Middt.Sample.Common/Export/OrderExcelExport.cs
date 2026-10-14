using Middt.Framework.Common.Export;

namespace Middt.Sample.Common.Export;

public class OrderExcelExport : BaseExcelExport
{
    public override string Folder => "Files";

    public override string TemplateFileName => "ExcelExport.xlsx";
}
