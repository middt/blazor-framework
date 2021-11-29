using Middt.Framework.Model.Authentication;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Security
{
    public interface IBaseSessionState
    {
        Task<string> Token();
        Task<string> RefreshToken();

        Task<int> ExpiresIn();

        Task SetToken(TokenResponseModel tokenResponseModel);
        Task ClearToken();
    }
}
