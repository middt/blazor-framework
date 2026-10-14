using System.ComponentModel;

namespace Middt.Framework.Model.Authentication;

public class LoginRequestModel : BaseTokenRequestModel
{
    [Description("Kullanıcı adınız")]
    public string? username { get; set; }

    [Description("Şifreniz")]
    public string? password { get; set; }
}
