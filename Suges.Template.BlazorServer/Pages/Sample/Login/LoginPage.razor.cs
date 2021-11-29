using Microsoft.AspNetCore.Components;
using Suges.Framework.Blazor.Web.Base.Component.Captcha;
using Suges.Framework.Blazor.Web.Base.Page;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Security.Refit;
using Suges.Framework.Model.Authentication;

namespace Suges.Template.BlazorServer.Pages.Sample.Login
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

                if (tokenResponseModel.Result == Suges.Framework.Model.Model.Enumerations.ResultEnum.Success)
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
