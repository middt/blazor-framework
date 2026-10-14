using Middt.Framework.Common.Export;

namespace Middt.Sample.Common.Export;

public class OrderPdfExport : BaseWordToPdfExport
{
    public override string Folder => "Files";

    public override string TemplateFileName => "WordExport.docx";
}
