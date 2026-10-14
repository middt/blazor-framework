#nullable disable
using System.ComponentModel.DataAnnotations.Schema;
using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;

namespace Middt.Sample.Api.Model.Database
{
    public partial class Customer : BaseRequestModel
    {
        [NotMapped]
        [QueryIsNoFilter]
        public string Name
        {

            get
            {
                return FirstName + " " + LastName;
            }
        }
    }
}
