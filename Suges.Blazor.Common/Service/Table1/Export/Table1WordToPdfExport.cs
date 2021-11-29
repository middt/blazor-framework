using Suges.Framework.Common.Export;

namespace Suges.Template.Common.Service
{
    public class Table1WordToPdfExport : BaseWordToPdfExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokumWord.docx";
    }
}
