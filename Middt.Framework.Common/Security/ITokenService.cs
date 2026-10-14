using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Common.Security;

public interface ITokenService
{
    Task<TokenResponseModel> LoginAsync(LoginRequestModel request, CancellationToken cancellationToken = default);

    Task<TokenResponseModel> RefreshTokenAsync(TokenRefreshRequestModel request, CancellationToken cancellationToken = default);

    /// <summary>Revokes the refresh token family of the current session (logout). Failures are logged, never thrown.</summary>
    Task RevokeAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default);
}
