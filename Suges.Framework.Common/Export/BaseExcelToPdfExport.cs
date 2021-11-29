using Suges.Framework.Common.Model.Data;
using Syncfusion.Pdf;
using Syncfusion.XlsIO;
using Syncfusion.XlsIORenderer;
using System;
using System.IO;


namespace Suges.Framework.Common.Export
{
    public abstract class BaseExcelToPdfExport : BaseExcelExport
    {

        public override BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
        {
            BaseResponseDataModel<byte[]> response = new BaseResponseDataModel<byte[]>();

            try
            {
                BaseResponseDataModel<byte[]> baseResponseDataModel = ConvertToExcel(baseExportModel);
                using (MemoryStream ms = new MemoryStream(baseResponseDataModel.Data))
                {
                    using (ExcelEngine excelEngine = new ExcelEngine())
                    {
                        IApplication application = excelEngine.Excel;
                        IWorkbook workbook = application.Workbooks.Open(ms);

                        //Initialize XlsIO renderer.
                        XlsIORenderer renderer = new XlsIORenderer();
                        //Convert Excel document into PDF document 
                        PdfDocument pdfDocument = renderer.ConvertToPDF(workbook);
                        using (MemoryStream pdfStream = new MemoryStream())
                        {
                            pdfDocument.Save(pdfStream);
                            response.Data = pdfStream.ToArray();
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                response.Result = Framework.Model.Model.Enumerations.ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }

            return response;
        }
    }
}
