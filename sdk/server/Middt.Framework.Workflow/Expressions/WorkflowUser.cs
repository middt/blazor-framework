using System.Globalization;
using System.Security.Claims;
using Middt.Framework.Expressions;

namespace Middt.Framework.Workflow.Expressions;

/// <summary>The caller as seen by expressions: <c>user.Name</c>, <c>user.Id</c>, <c>user.IsInRole("manager")</c>.</summary>
public sealed class WorkflowUser : ExpressionUser
{
    public static WorkflowUser System { get; } = new() { Id = "system", Name = "system", IsSystem = true };

    public static WorkflowUser From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new WorkflowUser { Name = "anonymous" };
        }

        return new WorkflowUser
        {
            Id = principal.FindFirst(ClaimTypes.Sid)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Name = principal.Identity.Name,
            Roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        };
    }

    /// <summary>Name recorded in history/StartedBy; system actors acting on behalf of a user (queued transitions, subflows) keep that user's name.</summary>
    internal string AuditName => IsSystem ? Name ?? "system" : Name ?? Id ?? "anonymous";

    internal static string Describe(long id) => id.ToString(CultureInfo.InvariantCulture);
}
