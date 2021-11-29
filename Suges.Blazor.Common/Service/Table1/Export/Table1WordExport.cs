using Suges.Framework.Common.Export;

namespace Suges.Template.Common.Service
{
    public class Table1WordExport : BaseWordExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokumWord.docx";
    }
}
