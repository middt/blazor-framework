#nullable disable
using System.ComponentModel.DataAnnotations.Schema;
using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;

namespace Middt.Sample.Api.Model.Database
{
    public partial class Product : BaseRequestModel, ILifecycleRecord
    {
        /// <summary>Passive records (closed by the lifecycle workflow) stay for the existing orders but are not offered in lookups.</summary>
        [QueryIsNoFilter]
        public bool Active { get; set; } = true;

        /// <inheritdoc />
        [NotMapped]
        [QueryIsNoFilter]
        public string Status { get; set; }
    }
}
