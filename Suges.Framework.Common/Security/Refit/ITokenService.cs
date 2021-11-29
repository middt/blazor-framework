using Suges.Framework.Model.Authentication;

namespace Suges.Framework.Common.Security.Refit
{
    public interface ITokenService
    {
        TokenResponseModel Login(LoginRequestModel request);
        TokenResponseModel RefreshToken(TokenRefreshRequestModel request);

    }
}
