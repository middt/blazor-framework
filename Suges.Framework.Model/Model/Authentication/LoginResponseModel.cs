using Suges.Framework.Common.Model.Data;
using System;

namespace Suges.Framework.Model.Authentication
{
    public class LoginResponseModel : BaseResponseModel
    {
        public Int64? UserID { get; set; }
        public short? Rol { get; set; }
        public Int64? FirmaID { get; set; }
        public string Username { get; set; }


    }
}
