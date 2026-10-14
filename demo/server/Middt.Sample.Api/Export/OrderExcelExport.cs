using Middt.Framework.Common.Export;

namespace Middt.Sample.Api.Export;

public class OrderExcelExport : BaseExcelExport
{
    public override string Folder => "Files";

    public override string TemplateFileName => "ExcelExport.xlsx";
}
