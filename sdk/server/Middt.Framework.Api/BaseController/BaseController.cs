using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Middt.Framework.Api;

[ApiController]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{
    protected int UserId =>
        User.FindFirst(ClaimTypes.Sid) is { } claim && int.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : -1;

    /// <summary>Subject identifier (<see cref="ClaimTypes.Sid"/> or <see cref="ClaimTypes.NameIdentifier"/>), regardless of whether it is numeric or a GUID/string.</summary>
    protected string? SubjectId =>
        User.FindFirst(ClaimTypes.Sid)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    protected IReadOnlyList<string> RoleList => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    protected bool IsInRole(string role) => User.IsInRole(role);
}
