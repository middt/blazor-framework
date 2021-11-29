using Swashbuckle.AspNetCore.Annotations;

namespace Suges.Framework.Model.Model.Authentication
{
    public class SwaggerLoginRequestModel
    {
        [SwaggerSchema("Kullanıcı Adınız")]
        public string username { get; set; }
        [SwaggerSchema("Şifreniz")]
        public string password { get; set; }
    }
}
