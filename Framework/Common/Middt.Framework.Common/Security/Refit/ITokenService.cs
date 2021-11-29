using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Common.Security.Refit
{
    public interface ITokenService
    {
        TokenResponseModel Login(LoginRequestModel request);
        TokenResponseModel RefreshToken(TokenRefreshRequestModel request);

    }
}
