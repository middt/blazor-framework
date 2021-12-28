using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Authentication;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Security.Refit
{
    public abstract class BaseTokenService : BaseRefit<IBaseTokenRefit>, ITokenService
    {
        public BaseTokenService(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {



        }

        public async Task<TokenResponseModel> Login(string version, LoginRequestModel request)
        {
            return await ExecutePolly(() =>
             {
                 return api.Login(version, controllerName, request).Result;
             }
            );
        }
        public async Task<TokenResponseModel> Login(LoginRequestModel request)
        {
            return await Login("1", request);
        }


        public async Task<TokenResponseModel> RefreshToken(string version, TokenRefreshRequestModel request)
        {
            return await ExecutePolly(() =>
            {
                return api.RefreshToken(version, controllerName, request).Result;
            }
           );
        }
        public async Task<TokenResponseModel> RefreshToken(TokenRefreshRequestModel request)
        {
            return await RefreshToken("1", request);
        }
    }
}
