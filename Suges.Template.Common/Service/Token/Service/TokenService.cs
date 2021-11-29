using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Security.Refit;

namespace Suges.Template.Common.Service
{
    public class TokenService : BaseTokenService
    {
        public override string controllerName => "token";
        public TokenService(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }
    }
}