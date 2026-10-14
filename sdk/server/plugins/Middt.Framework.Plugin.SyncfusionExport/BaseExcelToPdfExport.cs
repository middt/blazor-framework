using Middt.Framework.Common.Model.Data;
using Syncfusion.XlsIO;
using Syncfusion.XlsIORenderer;

namespace Middt.Framework.Common.Export;

public abstract class BaseExcelToPdfExport : BaseExcelExport
{
    public override BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
    {
        var excel = ConvertToExcel(baseExportModel);
        if (!excel.IsSuccess || excel.Data is null)
        {
            return excel;
        }

        using var excelStream = new MemoryStream(excel.Data);
        using var excelEngine = new ExcelEngine();
        var workbook = excelEngine.Excel.Workbooks.Open(excelStream);

        using var pdfDocument = new XlsIORenderer().ConvertToPDF(workbook);
        using var pdfStream = new MemoryStream();
        pdfDocument.Save(pdfStream);
        return BaseResponseDataModel<byte[]>.Success(pdfStream.ToArray());
    }
}
