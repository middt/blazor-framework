using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Model.Authentication;

public class LoginResponseModel : BaseResponseModel
{
    public long? UserID { get; set; }
    public short? Rol { get; set; }
    public long? FirmaID { get; set; }
    public string? Username { get; set; }
}
