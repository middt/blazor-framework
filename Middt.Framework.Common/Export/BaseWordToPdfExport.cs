using Middt.Framework.Common.Model.Data;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using System;
using System.IO;


namespace Middt.Framework.Common.Export
{
    public abstract class BaseWordToPdfExport : BaseWordExport
    {
        public override BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
        {
            BaseResponseDataModel<byte[]> response = new BaseResponseDataModel<byte[]>();

            try
            {
                BaseResponseDataModel<byte[]> baseResponseDataModel = ConvertToWord(baseExportModel);
                using (MemoryStream wordStream = new MemoryStream(baseResponseDataModel.Data))
                {
                    using (WordDocument wordDocument = new WordDocument(wordStream, FormatType.Docx))
                    {
                        using (DocIORenderer renderer = new DocIORenderer())
                        {
                            PdfDocument pdfDocument = renderer.ConvertToPDF(wordDocument);

                            using (MemoryStream pdfStream = new MemoryStream())
                            {
                                pdfDocument.Save(pdfStream);
                                response.Data = pdfStream.ToArray();
                            }
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
