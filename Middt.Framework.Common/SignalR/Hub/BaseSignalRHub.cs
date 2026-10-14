using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Middt.Framework.Common.SignalR.Model;

namespace Middt.Framework.Common.SignalR;

/// <summary>
/// Strongly typed hub that relays <see cref="BaseSignalRRequestModel{TModel}"/> messages to all clients,
/// specific users (by <see cref="ClaimTypes.NameIdentifier"/>) or groups.
/// Scale out with a backplane (e.g. <c>AddSignalR().AddStackExchangeRedis()</c>).
/// </summary>
public class BaseSignalRHub<TModel> : Hub<IBaseSignalRClient<TModel>>, IBaseSignalRHub<TModel>
{
    protected ClaimsPrincipal? CurrentUser => Context.User;

    public string UserName => CurrentUser?.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    public int UserId =>
        CurrentUser?.FindFirst(ClaimTypes.Sid) is { } claim && int.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : -1;

    public IReadOnlyList<string> UserRoles =>
        CurrentUser?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsUserInRole(string role) => CurrentUser?.IsInRole(role) ?? false;

    public virtual Task Send(BaseSignalRRequestModel<TModel> sendModel)
    {
        ArgumentNullException.ThrowIfNull(sendModel);

        return sendModel.MessageType switch
        {
            MessageType.User => Clients.Users(sendModel.MessageTo).Receive(sendModel.Data!),
            MessageType.Group => Clients.Groups(sendModel.MessageTo).Receive(sendModel.Data!),
            _ => Clients.All.Receive(sendModel.Data!),
        };
    }

    public virtual Task JoinGroup(string groupName) => Groups.AddToGroupAsync(Context.ConnectionId, groupName);

    public virtual Task LeaveGroup(string groupName) => Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
}
