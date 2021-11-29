using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;

namespace Suges.Framework.Common.Security
{
    public class BaseTokenHelper
    {

        protected IBaseSessionState protectedLocalStorage { get; set; }

        public ClaimsIdentity claimsIdentity { get; set; }

        public BaseTokenHelper(IBaseSessionState _protectedLocalStorage)
        {
            protectedLocalStorage = _protectedLocalStorage;
        }
        public void GetClaimsPrincipal()
        {
            if (protectedLocalStorage == null)
            {
                claimsIdentity = new ClaimsIdentity();
                return;
            }

            string token = protectedLocalStorage.Token().Result;
            if (!string.IsNullOrEmpty(token))
            {
                claimsIdentity = new ClaimsIdentity(ParseClaimsFromJwt(token), "jwt");
            }
            else
            {
                claimsIdentity = new ClaimsIdentity();
            }
        }

        public Int64 UserID()
        {
            Claim userID =  claimsIdentity.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Sid);

            if (userID != null)
                return Convert.ToInt64(userID.Value);
            else
                return -1;
        }
        public string Username()
        {
            Claim userID = claimsIdentity.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name);

            if (userID != null)
                return userID.Value;
            else
                return string.Empty;
        }


        public List<string> RoleList()
        {
            return claimsIdentity.Claims.Where(x => x.Type == ClaimTypes.Role).Select(x => x.Value).ToList();
        }
        public bool IsHasRoles(List<string> roles)
        {
            List<string> roleList = RoleList();

            if (roleList != null && roleList.Count > 0)
            {
                return roleList.Any(x => roles.Contains(x));
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
                return roleList.Any(x => x.Equals(role));
            }
            else
            {
                return false;
            }
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
