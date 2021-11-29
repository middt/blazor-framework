using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Middt.Template.Api.Model;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace Middt.Template.Api.Controllers
{
    [ApiController]
    //[Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class TokenController : BaseTokenController
    {
        protected override LoginResponseModel CheckUsernamePassword(LoginRequestModel loginRequestModel)
        {
            LoginResponseModel loginResponseModel = new LoginResponseModel();
            try
            {
                if (loginRequestModel.username == "test@test.com" && loginRequestModel.password == "test")
                {
                    loginResponseModel.UserID = 1;
                    loginResponseModel.Rol = 1;
                    loginResponseModel.FirmaID = 1;
                    loginResponseModel.Username = "test@test.com";
                }
                else
                {

                    loginResponseModel.Result = ResultEnum.Error;
                    loginResponseModel.MessageList.Add("Kullanıcı adı veya şifreniz yanlış.");
                }
            }
            catch (Exception ex)
            {
                loginResponseModel.Result = ResultEnum.Error;
                loginResponseModel.MessageList.Add(ex.ToString());

                // LogHelper.Instance.Error(ex.ToString());
            }
            return loginResponseModel;
        }

        protected override void AddClaimList(LoginResponseModel loginResponseModel)
        {
            base.AddClaimList(loginResponseModel);

            //ClaimList.Add(new Claim("Role", loginResponseModel.Rol.ToString()));
            ClaimList.Add(new Claim(ClaimTypes.Role, loginResponseModel.Rol.ToString()));
            //ClaimList.Add(new Claim("Username", loginResponseModel.Username));

            if (loginResponseModel.FirmaID.HasValue)
                ClaimList.Add(new Claim("FirmaId", loginResponseModel.FirmaID.Value.ToString()));
        }

        protected override LoginResponseModel CheckRefreshToken(TokenRefreshRequestModel tokenRefreshRequestModel, long UsersID)
        {
            LoginResponseModel loginResponseModel = new LoginResponseModel();

            // test amaçlı eklenmiştir. DB varken Refresh token dbde var mı kontrol edilecek
            //
            if (tokenRefreshRequestModel.refresh_token != null)
            {
                loginResponseModel.UserID = UsersID;
                loginResponseModel.FirmaID = 1;
                loginResponseModel.Rol = 1;
                loginResponseModel.Username = "test@test.com";
            }
            else
            {
                loginResponseModel.Result = ResultEnum.Error;
                loginResponseModel.MessageList.Add("Refresh token bilgisi bulunamadı.");
            }
            //
            return loginResponseModel;
        }

        protected override void SaveRefreshToken(TokenResponseModel tokenResponseModel, Int64 UsersID)
        {
            try
            {
                YenilemeToken refreshToken = new YenilemeToken
                {
                    EklenmeTarihi = DateTime.Now,
                    YenilemeToken1 = tokenResponseModel.refresh_token,
                    Token = tokenResponseModel.access_token,
                    KullaniciId = UsersID
                };

                //DB kaydedilecek
            }
            catch (Exception ex)
            {
                // LogHelper.Instance.Error(ex.ToString());
            }
        }

        protected override List<TokenResponseAdditionalDataModel> AdditionalData(LoginRequestModel request)
        {
            // throw new NotImplementedException();

            return null;
        }
    }
}