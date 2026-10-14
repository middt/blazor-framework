using Middt.Framework.Common.Model.Data;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;

namespace Middt.Framework.Common.Export;

public abstract class BaseWordToPdfExport : BaseWordExport
{
    public override BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
    {
        var word = ConvertToWord(baseExportModel);
        if (!word.IsSuccess || word.Data is null)
        {
            return word;
        }

        using var wordStream = new MemoryStream(word.Data);
        using var wordDocument = new WordDocument(wordStream, FormatType.Docx);
        using var renderer = new DocIORenderer();
        using var pdfDocument = renderer.ConvertToPDF(wordDocument);
        using var pdfStream = new MemoryStream();
        pdfDocument.Save(pdfStream);
        return BaseResponseDataModel<byte[]>.Success(pdfStream.ToArray());
    }
}
