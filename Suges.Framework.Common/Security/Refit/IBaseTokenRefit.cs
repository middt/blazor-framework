using RestEase;
using Suges.Framework.Model.Authentication;
using System.Threading.Tasks;

namespace Suges.Framework.Common.Security.Refit
{
    public interface IBaseTokenRefit
    {
        [Post("/api/v{version}/{controllerName}/login")]
        Task<TokenResponseModel> Login([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] LoginRequestModel request);

        [Post("/api/v{version}/{controllerName}/refreshtoken")]
        Task<TokenResponseModel> RefreshToken([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] TokenRefreshRequestModel request);
    }
}
