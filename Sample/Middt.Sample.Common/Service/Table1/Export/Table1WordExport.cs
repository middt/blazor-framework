using Middt.Framework.Common.Export;

namespace Middt.Sample.Common.Service
{
    public class Table1WordExport : BaseWordExport
    {
        public override string Folder => @"Files\";

        public override string TemplateFileName => "SozlesmeDokumWord.docx";
    }
}
