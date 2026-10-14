using System.Security.Claims;

namespace Middt.Framework.Expressions;

/// <summary>The caller as seen by expressions: <c>user.Name</c>, <c>user.Id</c>, <c>user.IsInRole("manager")</c>.</summary>
public class ExpressionUser
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>System actors pass every role check.</summary>
    public bool IsSystem { get; init; }

    public bool IsInRole(string role) => IsSystem || Roles.Contains(role, StringComparer.Ordinal);

    /// <summary>Builds the expression user of an authenticated principal (anonymous otherwise).</summary>
    public static ExpressionUser FromPrincipal(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
            ? new ExpressionUser
            {
                Id = principal.FindFirst(ClaimTypes.Sid)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Name = principal.Identity.Name,
                Roles = [.. principal.FindAll(ClaimTypes.Role).Select(c => c.Value)],
            }
            : new ExpressionUser { Name = "anonymous" };

    public override string ToString() => Name ?? Id ?? string.Empty;
}
