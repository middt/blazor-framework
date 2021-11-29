using Suges.Framework.Model.Model.Enumerations;

namespace Suges.Framework.Blazor.Web.Model
{
    public class FileUploadModel
    {
        public int FileUploadModelID { get; set; }
        public string Name { get; set; }

        public string FilePath { get; set; }

        public YesNoEnum IsDeleted { get; set; }
        public byte[] File { get; set; }

    }
}
