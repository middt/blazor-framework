using Middt.Framework.Common.Export;

namespace Middt.Sample.Api.Export;

public class OrderWordExport : BaseWordExport
{
    public override string Folder => "Files";

    public override string TemplateFileName => "WordExport.docx";
}
