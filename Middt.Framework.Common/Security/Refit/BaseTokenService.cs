using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Authentication;

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

        public virtual TokenResponseModel Login(string version, LoginRequestModel request)
        {
            return ExecutePolly(() =>
             {
                 return api.Login(version, controllerName, request).Result;
             }
            );
        }
        public virtual TokenResponseModel Login(LoginRequestModel request)
        {
            return Login("1", request);
        }


        public virtual TokenResponseModel RefreshToken(string version, TokenRefreshRequestModel request)
        {
            return ExecutePolly(() =>
            {
                return api.RefreshToken(version, controllerName, request).Result;
            }
           );
        }
        public virtual TokenResponseModel RefreshToken(TokenRefreshRequestModel request)
        {
            return RefreshToken("1", request);
        }
    }
}
