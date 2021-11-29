using Suges.Framework.Blazor.Web.Model;
using Suges.Framework.Common.Database.Attributes;
using Suges.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Suges.Template.Api.Model.Database
{

    public partial class Table1Secure : BaseRequestModel
    {
        //[QueryIsNoFilter]
        [QueryDate(DateSearchType.GreaterThanOrEqual, nameof(Tarih))]
        [NotMapped]
        public DateTime? StartDate { get; set; }


        [QueryDate(DateSearchType.LessThanOrEqual, nameof(Tarih))]
        [NotMapped]
        public DateTime? EndDate { get; set; }
        //[ArrayMinLength(1, ErrorMessage = "En Az 1 dosya yüklemelisiniz.")]

        [QueryIsNoFilter]
        [NotMapped]
        public List<FileUploadModel> ListFileUploadModel { get; set; }

        public Table1Secure()
        {
            ListFileUploadModel = new List<FileUploadModel>();
        }
    }
}
