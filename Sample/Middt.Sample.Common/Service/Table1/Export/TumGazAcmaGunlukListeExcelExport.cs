using Middt.Framework.Common.Export;
using Syncfusion.DocIO.DLS;
using Syncfusion.Drawing;
using Syncfusion.XlsIO;
using System.Data;

namespace Middt.Template.Common.Service
{
    public class TumGazAcmaGunlukListeExcelExport : BaseExcelExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "TumGunlukListe.xlsx";

        public override void CustomConvert(BaseExportModel baseExportModel, IWorkbook workbook, ITemplateMarkersProcessor marker)
        {
            base.CustomConvert(baseExportModel, workbook, marker);

            ExportParameterModel exportParameterModel = baseExportModel.ParamList.Find(x => x.Key == "Randevu");
            DataSet ds = exportParameterModel.Value as DataSet;

            Syncfusion.XlsIO.IStyle style = workbook.Styles.Add("NewStyle");
            style.Color = Color.Yellow;
            style.Font.Bold = true;


            style.HorizontalAlignment = (ExcelHAlign)HorizontalAlignment.Center;
            style.VerticalAlignment = (ExcelVAlign)VerticalAlignment.Middle;

            IWorksheet worksheet = workbook.Worksheets[0];
            int row = 2, col = 14;
            int rowCount = 0;


            foreach (DataTable dt in ds.Tables)
            {
                worksheet.Range[row, col].Text = dt.TableName;

                rowCount = dt.Rows.Count;
                IRange range = worksheet.Range[row, col, row + rowCount - 1, col];
                range.Merge();
                range.VerticalAlignment = ExcelVAlign.VAlignTop;
                range.HorizontalAlignment = ExcelHAlign.HAlignCenter;
                range.CellStyle = style;

                range.BorderAround(ExcelLineStyle.Medium);

                row += rowCount;
            }

            worksheet.UsedRange.AutofitColumns();
            worksheet.UsedRange.AutofitRows();
        }
    }
}
