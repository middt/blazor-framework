using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Suges.Framework.Api
{
    public abstract class BaseController : Controller
    {
        protected int UserId
        {
            get
            {
                var claim = ((ClaimsIdentity)User.Identity).FindFirst(ClaimTypes.Sid);

                if (claim != null)
                    return Convert.ToInt32(claim.Value);
                else
                    return -1;
            }
        }

        protected List<string> RoleList
        {
            get
            {
                List<string> roleList = ((ClaimsIdentity)User.Identity).Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

                if (roleList != null)
                    return roleList;
                else
                    return new List<string>();
            }
        }

        protected bool IsInRole(string role)
        {
            return RoleList.Contains(role);
        }
    }
}
