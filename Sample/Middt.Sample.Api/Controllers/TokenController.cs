using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Api.Security;
using Middt.Framework.Model.Authentication;

namespace Middt.Sample.Api.Controllers;

/// <summary>
/// Demo token endpoint with a single hard coded user (<c>test@test.com</c> / <c>test</c>).
/// A real application checks a salted password hash (e.g. ASP.NET Core Identity's <c>PasswordHasher</c>) in its user store.
/// Refresh token rotation, reuse detection and revocation are handled by the framework.
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class TokenController(IJwtTokenGenerator tokenGenerator) : BaseTokenController(tokenGenerator)
{
    private const long DemoUserId = 1;
    private const string DemoUser = "test@test.com";

    protected override Task<LoginResponseModel> CheckUsernamePasswordAsync(LoginRequestModel request, CancellationToken cancellationToken) =>
        Task.FromResult(request is { username: DemoUser, password: "test" } ? DemoLogin() : new LoginResponseModel());

    protected override Task<LoginResponseModel> LoadUserAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(userId == DemoUserId ? DemoLogin() : new LoginResponseModel());

    protected override IEnumerable<System.Security.Claims.Claim> CreateClaims(LoginResponseModel login) =>
        base.CreateClaims(login).Append(new System.Security.Claims.Claim("FirmaId", login.FirmaID?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"));

    private static LoginResponseModel DemoLogin() => new()
    {
        UserID = DemoUserId,
        Rol = 1,
        FirmaID = 1,
        Username = DemoUser,
    };
}
