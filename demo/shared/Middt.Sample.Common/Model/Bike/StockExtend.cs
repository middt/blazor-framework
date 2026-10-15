#nullable disable
using System.ComponentModel.DataAnnotations.Schema;
using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;

namespace Middt.Sample.Api.Model.Database
{
    public partial class Stock : BaseRequestModel
    {
        /// <summary>Filled by the stock list projection (StockRepository.GetAll); ignored on writes.</summary>
        [NotMapped]
        [QueryIsNoFilter]
        public string StoreName { get; set; }

        /// <summary>Filled by the stock list projection (StockRepository.GetAll); ignored on writes.</summary>
        [NotMapped]
        [QueryIsNoFilter]
        public string ProductName { get; set; }
    }
}
