using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Suges.Framework.Common.Security;
using Suges.Framework.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Security
{
    //public class BaseProtectedSessionStorage : IBaseSessionState
    //{
    //    ProtectedSessionStorage protectedSessionStorage;
    //    public BaseProtectedSessionStorage(ProtectedSessionStorage _protectedSessionStorage)
    //    {

    //        protectedSessionStorage = _protectedSessionStorage;
    //    }
    //    public string Token
    //    {
    //        get
    //        {
    //            try
    //            {
    //                return protectedSessionStorage.GetAsync<string>("access_token").Result.Value;
    //            }
    //            catch (Exception ex)
    //            {
    //                return null;
    //            }
    //        }
    //    }
    //    public string RefreshToken
    //    {
    //        get
    //        {
    //            try
    //            {
    //                return protectedSessionStorage.GetAsync<string>("refresh_token").Result.Value;
    //            }
    //            catch (Exception ex)
    //            {
    //                return null;
    //            }
    //        }
    //    }

    //    public void ClearToken()
    //    {
    //        protectedSessionStorage.DeleteAsync("access_token");
    //        protectedSessionStorage.DeleteAsync("refresh_token");
    //    }

    //    public void SetToken(TokenResponseModel tokenResponseModel)
    //    {
    //        protectedSessionStorage.SetAsync("access_token", tokenResponseModel.access_token);
    //        protectedSessionStorage.SetAsync("refresh_token", tokenResponseModel.refresh_token);
    //    }
    //}
}
