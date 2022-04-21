using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Captcha;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Security.Refit;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using System.Threading.Tasks;

namespace Middt.Sample.BlazorServer.Pages.Sample.Login
{


    public partial class LoginPage : BasePageComponent
    {
        [Inject]
        protected ITokenService tokenService { get; set; }

        [Inject]
        protected IBaseSessionState baseSessionState { get; set; }

        public LoginRequestModel loginRequestModel;

        public LoginPage()
        {
            loginRequestModel = new LoginRequestModel();
            loginRequestModel.username = "test@test.com";
            loginRequestModel.password = "test";
        }

        public async Task LoginSite()
        {
            await ExecuteMethod(async() =>
            {
                TokenResponseModel tokenResponseModel = await tokenService.Login(loginRequestModel);

                if (tokenResponseModel.Result == ResultEnum.Success)
                {

                    await baseSessionState.SetToken(tokenResponseModel);
                    string returnUrl = NavigationManager.QueryString("returnUrl");

                    if (string.IsNullOrEmpty(returnUrl))
                        returnUrl = "/";

                    NavigationManager.NavigateTo(returnUrl);

                }
            });
        }
    }
}
