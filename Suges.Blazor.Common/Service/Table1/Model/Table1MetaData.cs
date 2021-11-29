using Suges.Framework.Blazor.Web.Model;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Suges.Template.Api.Model.Database
{
    public class Table1MetaData
    {
        [Required(ErrorMessage = "Lütfen geçerli bir ad giriniz.")]
        //[CustomTable1RemoteValidationAttribute("Custom Validation")]
        public string Ad { get; set; }


        //[ArrayMinLength(1, ErrorMessage = "En Az 1 dosya yüklemelisiniz.")]
        //[ArrayMaxLength(2, ErrorMessage = "En çok 2 dosya yüklemelisiniz.")]
        public List<FileUploadModel> ListFileUploadModel { get; set; }
    }
}
