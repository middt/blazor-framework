using Microsoft.AspNetCore.Components.Authorization;
using Middt.Framework.Common.Dependency;
using Middt.Framework.Common.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Security
{
    public class FrameworkAuthenticationStateProvider : AuthenticationStateProvider
    {
        IBaseSessionState baseSessionState;
        public FrameworkAuthenticationStateProvider(IBaseSessionState _baseSessionState)
        {
            baseSessionState = _baseSessionState;


        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ClaimsPrincipal claimsPrincipal = GetClaimsPrincipal(baseSessionState.Token().Result);
            return Task.FromResult(new AuthenticationState(claimsPrincipal));
        }

        public ClaimsPrincipal GetClaimsPrincipal(string token)
        {
            ClaimsIdentity identity = null;
            try
            {
                if (!string.IsNullOrEmpty(token))
                {
                    identity = new ClaimsIdentity(ParseClaimsFromJwt(token), "jwt");
                }
                else
                {
                    identity = new ClaimsIdentity();
                }
            }
            catch (Exception ex)
            {


            }
            return new ClaimsPrincipal(identity);
        }

        public List<string> RoleList()
        {
            ClaimsPrincipal claimsPrincipal = GetClaimsPrincipal(baseSessionState.Token().Result);
            return claimsPrincipal.Claims.Where(x => x.Type == ClaimTypes.Role).Select(x => x.Value).ToList();
        }
        public bool IsHasRoles(List<string> roles)
        {
            List<string> roleList = RoleList();

            if (roleList != null && roleList.Count > 0)
            {
                return roleList.Where(x => roles.Contains(x)).Any();
            }
            else
            {
                return false;
            }
        }
        public bool IsHasRole(string role)
        {
            List<string> roleList = RoleList();
            if (roleList != null && roleList.Count > 0)
            {
                return roleList.Where(x => x.Contains(role)).Any();
            }
            else
            {
                return false;
            }
        }
        public void Logout()
        {
            IBaseSessionState baseSessionState = FrameworkDependencyHelper.Instance.Get<IBaseSessionState>();
            baseSessionState.ClearToken();
        }

        private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            keyValuePairs.TryGetValue(ClaimTypes.Role, out object roles);

            if (roles != null)
            {
                if (roles.ToString().Trim().StartsWith("["))
                {
                    var parsedRoles = JsonSerializer.Deserialize<string[]>(roles.ToString());

                    foreach (var parsedRole in parsedRoles)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, parsedRole));
                    }
                }
                else
                {
                    claims.Add(new Claim(ClaimTypes.Role, roles.ToString()));
                }

                keyValuePairs.Remove(ClaimTypes.Role);
            }

            claims.AddRange(keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString())));

            return claims;
        }

        private byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}