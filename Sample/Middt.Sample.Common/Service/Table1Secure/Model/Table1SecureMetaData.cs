using Middt.Framework.Blazor.Web.Model;
using Middt.Framework.Common.Validation;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Middt.Sample.Api.Model.Database
{
    public class Table1SecureMetaData
    {
        [Required(ErrorMessage = "Lütfen geçerli bir ad giriniz.")]
        //[CustomTable1RemoteValidationAttribute("Custom Validation")]
        public string Ad { get; set; }

        [ArrayMinLength(1, ErrorMessage = "En Az 1 dosya yüklemelisiniz.")]
        [ArrayMaxLength(2, ErrorMessage = "En çok 2 dosya yüklemelisiniz.")]
        [Required]
        public List<FileUploadModel> ListFileUploadModel { get; set; }
    }
}
