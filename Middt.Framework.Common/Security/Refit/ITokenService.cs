using Middt.Framework.Model.Authentication;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Security.Refit
{
    public interface ITokenService
    {
        Task<TokenResponseModel> Login(LoginRequestModel request);
        Task<TokenResponseModel> RefreshToken(TokenRefreshRequestModel request);

    }
}
