﻿using System;
using System.Collections.Generic;

namespace Middt.Sample.Api.Model.Database
{
    /// <summary>
    /// Lookup table of customer purchase reasons.
    /// </summary>
    public partial class SalesReason
    {
        public SalesReason()
        {
            SalesOrderHeaderSalesReasons = new HashSet<SalesOrderHeaderSalesReason>();
        }

        /// <summary>
        /// Primary key for SalesReason records.
        /// </summary>
        public int SalesReasonId { get; set; }
        /// <summary>
        /// Sales reason description.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Category the sales reason belongs to.
        /// </summary>
        public string ReasonType { get; set; }
        /// <summary>
        /// Date and time the record was last updated.
        /// </summary>
        public DateTime ModifiedDate { get; set; }

        public virtual ICollection<SalesOrderHeaderSalesReason> SalesOrderHeaderSalesReasons { get; set; }
    }
}
