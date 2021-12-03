using Middt.Framework.Common.Database.Attributes;
using Middt.Framework.Common.Model.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Middt.Sample.Api.Model.Database
{
    public partial class Staff : BaseRequestModel
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
