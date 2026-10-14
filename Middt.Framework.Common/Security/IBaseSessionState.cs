using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Common.Security;

/// <summary>Client side token store (e.g. protected browser storage in Blazor Server).</summary>
public interface IBaseSessionState
{
    Task<string> Token();

    Task<string> RefreshToken();

    Task<int> ExpiresIn();

    Task SetToken(TokenResponseModel tokenResponseModel);

    Task ClearToken();
}
