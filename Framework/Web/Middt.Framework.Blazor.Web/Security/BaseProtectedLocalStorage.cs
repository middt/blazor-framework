using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Newtonsoft.Json;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Security;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Configuration;

namespace Middt.Framework.Blazor.Web.Security
{
    public class BaseProtectedLocalStorage : IBaseSessionState
    {
        string storageName = "suges";
        SaveTokenResponseModel saveTokenResponseModel;
        ProtectedLocalStorage protectedLocalStorage;
        IBaseConfiguration baseConfiguration;
        NavigationManager navigationManager;

        public BaseProtectedLocalStorage(
            NavigationManager _navigationManager,
            ProtectedLocalStorage _protectedLocalStorage, 
            IBaseConfiguration _baseConfiguration)
        {
            navigationManager = _navigationManager;
            protectedLocalStorage = _protectedLocalStorage;
            baseConfiguration = _baseConfiguration;
        }
        public async Task<int> ExpiresIn()
        {
            GetToken();

            if (saveTokenResponseModel != null)
                return saveTokenResponseModel.expires_in;

            return -1;
        }

        public async Task<string> Token()
        {
            GetToken();

            if (saveTokenResponseModel != null)
                return saveTokenResponseModel.access_token;

            return string.Empty;
        }
        public async Task<string> RefreshToken()
        {
            GetToken();

            if (saveTokenResponseModel != null)
                return saveTokenResponseModel.refresh_token;

            return string.Empty;
        }

        public async Task ClearToken()
        {
            await protectedLocalStorage.DeleteAsync(storageName);

            SecuritySettings securitySettings = baseConfiguration.Get<SecuritySettings>();
            navigationManager.NavigateTo(securitySettings.LoginPageURL + $"?returnUrl={Uri.EscapeDataString(navigationManager.ToBaseRelativePath(navigationManager.Uri))}");
        }

        public async Task SetToken(TokenResponseModel _tokenResponseModel)
        {
            saveTokenResponseModel = new SaveTokenResponseModel(_tokenResponseModel);
            // saveTokenResponseModel.expire_datetime = DateTime.Now.AddSeconds(saveTokenResponseModel.expires_in + 60 * 60);

            GeneralSettings settings = baseConfiguration.Get<GeneralSettings>();
            int timeoutSeconds = settings.SessionTimeoutInMinute * 60;

            saveTokenResponseModel.expire_datetime = DateTime.Now.AddSeconds(saveTokenResponseModel.expires_in + timeoutSeconds);


            string token = JsonConvert.SerializeObject(saveTokenResponseModel);

            await protectedLocalStorage.SetAsync(storageName, token);
        }

        protected async Task GetToken()
        {
            if (saveTokenResponseModel == null)
            {
                saveTokenResponseModel = ReadToken().Result;
            }


            if ((saveTokenResponseModel != null && saveTokenResponseModel.expire_datetime == null) ||

                (saveTokenResponseModel != null && saveTokenResponseModel.expire_datetime != null && saveTokenResponseModel.expire_datetime < DateTime.Now)
                )
            {
                saveTokenResponseModel = null;
                await ClearToken();
            }
        }



        public async Task<SaveTokenResponseModel> ReadToken()
        {
            try
            {
                var result = await protectedLocalStorage.GetAsync<string>(storageName);
                if (result.Success && !string.IsNullOrEmpty(result.Value))
                {
                    return JsonConvert.DeserializeObject<SaveTokenResponseModel>(result.Value);
                }
            }
            catch (Exception ex)
            {
                return null;
            }
            return null;
        }


    }
}
