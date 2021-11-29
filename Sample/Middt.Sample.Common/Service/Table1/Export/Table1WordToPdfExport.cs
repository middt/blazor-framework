using Middt.Framework.Common.Export;

namespace Middt.Sample.Common.Service
{
    public class Table1WordToPdfExport : BaseWordToPdfExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokumWord.docx";
    }
}
