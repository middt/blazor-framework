using System;
using System.Collections.Generic;

namespace Middt.Sample.Api.Model.Database
{
    public partial class ViewOrder
    {
        public DateTime OrderDate { get; set; }
        public string StoreName { get; set; }
        public string CustomerFirstName { get; set; }
        public string CustomerLastName { get; set; }
        public string StaffFirstName { get; set; }
        public string StaffLastName { get; set; }
        public int OrderId { get; set; }
        public int? CustomerId { get; set; }
        public byte OrderStatus { get; set; }
        public int StoreId { get; set; }
        public int StaffId { get; set; }
    }
}
