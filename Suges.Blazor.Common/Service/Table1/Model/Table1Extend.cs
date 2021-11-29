using Suges.Framework.Blazor.Web.Model;
using Suges.Framework.Common.Database.Attributes;
using Suges.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Suges.Template.Api.Model.Database
{
    [MetadataType(typeof(Table1MetaData))]
    public partial class Table1 : BaseRequestModel
    {
        //[QueryIsNoFilter]
        [QueryDate(DateSearchType.GreaterThanOrEqual, nameof(Tarih))]
        [NotMapped]
        public DateTime? StartDate { get; set; }

        [QueryDate(DateSearchType.LessThanOrEqual, nameof(Tarih))]
        [NotMapped]
        public DateTime? EndDate { get; set; }

        [NotMapped]
        [QueryIsNoFilter]
        public List<FileUploadModel> ListFileUploadModel { get; set; }

        public Table1()
        {
            ListFileUploadModel = new List<FileUploadModel>();
            //ListFileUploadModel.Add(new FileUploadModel() { FileUploadModelID = 1, Name = "Test" });
        }
    }
}
