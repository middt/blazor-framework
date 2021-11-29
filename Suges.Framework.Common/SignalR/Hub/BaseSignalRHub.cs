using Microsoft.AspNetCore.SignalR;
using Suges.Framework.Common.SignalR.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Suges.Framework.Common.SignalR
{
    public class BaseSignalRHub<TModel> : Hub<IBaseSignalRClient<TModel>>, IBaseSignalRHub<TModel>
    {
        #region token
        public string UserName
        {
            get
            {
                var claim = ((ClaimsIdentity)Context.User.Identity).FindFirst("UserName");

                if (claim != null)
                    return claim.Value;
                else
                    return string.Empty;
            }
        }
        public int UserId
        {
            get
            {
                var claim = ((ClaimsIdentity)Context.User.Identity).FindFirst(ClaimTypes.Sid);

                if (claim != null)
                    return Convert.ToInt32(claim.Value);
                else
                    return -1;
            }
        }
        public List<string> UserRoles
        {
            get
            {
                List<Claim> claimList = ((ClaimsIdentity)Context.User.Identity).FindAll(ClaimTypes.Role).ToList();

                if (claimList != null && claimList.Count > 0)
                    return claimList.Select(x => x.Value).ToList();
                else
                    return new List<string>();
            }
        }
        public bool IsUserInRole(string RoleId)
        {
            if (UserRoles != null && UserRoles.Count > 0)
            {
                if (UserRoles.Contains(RoleId))
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            else
            {
                return false;
            }
        }
        #endregion

        public static readonly ConcurrentDictionary<string, string>
      ConnectedUsers = new ConcurrentDictionary<string, string>();

        public override Task OnConnectedAsync()
        {
            ConnectedUsers.GetOrAdd(UserName, Context.ConnectionId);
            return base.OnConnectedAsync();
        }
        public override Task OnDisconnectedAsync(Exception exception)
        {
            string id;
            ConnectedUsers.TryRemove(UserName, out id);
            return base.OnDisconnectedAsync(exception);
        }

        public Task Send(BaseSignalRRequestModel<TModel> sendModel)
        {
            switch (sendModel.MessageType)
            {
                case MessageType.User:
                    return Clients.Users(sendModel.MessageTo).Receive(sendModel.Data);
                case MessageType.Group:
                    return Clients.Groups(sendModel.MessageTo).Receive(sendModel.Data);
                default:
                    return Clients.All.Receive(sendModel.Data);
            }
        }
    }
}
