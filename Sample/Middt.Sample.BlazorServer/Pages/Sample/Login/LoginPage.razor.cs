using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Captcha;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Security.Refit;
using Middt.Framework.Model.Authentication;

namespace Middt.Template.BlazorServer.Pages.Sample.Login
{


    public partial class LoginPage : BasePageComponent
    {
        [Inject]
        protected ITokenService tokenService { get; set; }

        [Inject]
        protected IBaseSessionState baseSessionState { get; set; }

        public LoginRequestModel loginRequestModel;


        public bool isValid { get; set; }

        public BaseCaptcha baseCaptcha { get; set; }

        public void CaptchaChange(string value)
        {
            if (baseCaptcha != null)
                isValid = baseCaptcha.IsValid(value);
            else
                isValid = false;
        }
        public LoginPage()
        {
            loginRequestModel = new LoginRequestModel();
            loginRequestModel.username = "test@test.com";
            loginRequestModel.password = "test";
        }

        public void LoginSite()
        {
            ExecuteMethod(() =>
            {
                TokenResponseModel tokenResponseModel = tokenService.Login(loginRequestModel);

                if (tokenResponseModel.Result == Middt.Framework.Model.Model.Enumerations.ResultEnum.Success)
                {

                    baseSessionState.SetToken(tokenResponseModel);
                    string returnUrl = NavigationManager.QueryString("returnUrl");

                    if (string.IsNullOrEmpty(returnUrl))
                        returnUrl = "/";

                    NavigationManager.NavigateTo(returnUrl);

                }
            });
        }
    }
}
