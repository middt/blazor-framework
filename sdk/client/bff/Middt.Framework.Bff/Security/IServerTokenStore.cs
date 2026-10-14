using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Bff.Security;

/// <summary>
/// Server side storage of the API tokens of a signed-in browser session (BFF pattern): the browser only holds an
/// HttpOnly cookie with the session id, tokens never reach JavaScript.
/// </summary>
public interface IServerTokenStore
{
    /// <summary>
    /// Returns the session's tokens, or <c>null</c> when the session does not exist, was revoked or timed out.
    /// <paramref name="touch"/> marks user activity and extends the idle timeout.
    /// </summary>
    Task<SaveTokenResponseModel?> GetAsync(string sessionId, bool touch, CancellationToken cancellationToken = default);

    /// <summary>Stores tokens; <see cref="SaveTokenResponseModel.expire_datetime"/> is the absolute session expiry.</summary>
    Task SetAsync(string sessionId, SaveTokenResponseModel tokens, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
