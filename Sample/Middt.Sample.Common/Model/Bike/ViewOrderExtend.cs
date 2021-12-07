using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Middt.Sample.Api.Model.Database
{
    public partial class ViewOrder : BaseRequestModel
    {
        [NotMapped]
        [QueryIsNoFilter]
        public string CustomerName
        {

            get{
                return CustomerFirstName + " " + CustomerLastName;
            }
        }

        [NotMapped]
        [QueryIsNoFilter]
        public string StaffName
        {

            get
            {
                return StaffFirstName + " " + StaffLastName;
            }
        }

        [NotMapped]
        [QueryDateAttribute(DateSearchType.GreaterThanOrEqual, nameof(OrderDate))]
        public DateTime? OrderStartDate { get; set; }

        [NotMapped]
        [QueryDateAttribute(DateSearchType.LessThanOrEqual,nameof(OrderDate))]
        public DateTime? OrderEndDate { get; set; }
    }
}
