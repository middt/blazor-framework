using Swashbuckle.AspNetCore.Annotations;

namespace Suges.Framework.Model.Authentication
{
    public class LoginRequestModel : BaseTokenRequestModel
    {
        [SwaggerSchema("Kullanıcı Adınız")]
        public string username { get; set; }
        [SwaggerSchema("Şifreniz")]
        public string password { get; set; }
    }
}
