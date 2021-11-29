using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Swagger;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using Swashbuckle.AspNetCore.Filters;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Middt.Framework.Api
{
    public abstract class BaseTokenController : BaseController
    {
        protected List<Claim> ClaimList { get; set; }

        BaseApiConfiguration baseConfiguration;

        protected BaseTokenController()
        {
            ClaimList = new List<Claim>();
            baseConfiguration = new BaseApiConfiguration();
        }

        [HttpPost("[action]")]
        [SwaggerTagAttribute(true)]
        [Swashbuckle.AspNetCore.Annotations.SwaggerOperation(Summary = "Token Servisi")]
        [SwaggerRequestExample(typeof(SwaggerLoginRequestModel), typeof(LoginRequestModelSwaggerExample))]
        public virtual TokenResponseModel Login([FromBody] LoginRequestModel request)
        {

            TokenResponseModel tokenResponseModel = new TokenResponseModel();
            try
            {


                if (string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
                {
                    tokenResponseModel.Result = ResultEnum.Error;
                    tokenResponseModel.MessageList.Add("Kullanıcı adı veya şifre alanını boş geçemezsiniz.");

                    return tokenResponseModel;
                }
                LoginResponseModel loginResponseModel = CheckUsernamePassword(request);
                if (loginResponseModel.UserID.HasValue && loginResponseModel.UserID > 0)
                {
                    AddClaimList(loginResponseModel);
                    tokenResponseModel = CreateToken();
                    SaveRefreshToken(tokenResponseModel, loginResponseModel.UserID.Value);

                    tokenResponseModel.Result = ResultEnum.Success;
                    tokenResponseModel.listTokenResponseAdditionalDataModel = AdditionalData(request);
                    return tokenResponseModel;
                }
                else
                {
                    if (loginResponseModel.MessageList.Count > 0)
                        tokenResponseModel.MessageList.AddRange(loginResponseModel.MessageList);
                    tokenResponseModel.Result = ResultEnum.Error;
                    tokenResponseModel.MessageList.Add("Kullanıcı adı veya şifreniz sistemde bulunamadı.");


                }
            }
            catch (Exception ex)
            {
                tokenResponseModel.Result = Framework.Model.Model.Enumerations.ResultEnum.Error;
                tokenResponseModel.MessageList.Add(ex.Message);

            }
            return tokenResponseModel;
        }

        [HttpPost("[action]")]
        public TokenResponseModel RefreshToken([FromBody] TokenRefreshRequestModel request)
        {
            TokenResponseModel tokenResponseModel = new TokenResponseModel();
            if (string.IsNullOrEmpty(request.refresh_token) || string.IsNullOrEmpty(request.token))
            {
                tokenResponseModel.Result = ResultEnum.Error;
                tokenResponseModel.MessageList.Add("Token bilgilerini boş geçemezsiniz.");

                return tokenResponseModel;
            }

            ClaimsPrincipal claimsPrincipal = GetPrincipalFromExpiredToken(request.token);
            Claim UsersID = ((ClaimsIdentity)claimsPrincipal.Identity).FindFirst(ClaimTypes.Sid);

            if (UsersID != null)
            {
                LoginResponseModel loginResponseModel = CheckRefreshToken(request, Convert.ToInt64(UsersID.Value));

                if (loginResponseModel.UserID.HasValue && loginResponseModel.UserID > 0)
                {
                    AddClaimList(loginResponseModel);
                    tokenResponseModel = CreateToken();
                    SaveRefreshToken(tokenResponseModel, loginResponseModel.UserID.Value);

                    tokenResponseModel.Result = ResultEnum.Success;
                    return tokenResponseModel;
                }
                else
                {
                    tokenResponseModel.Result = ResultEnum.Error;
                    tokenResponseModel.MessageList.Add("Token bilgileriniz sistemde bulunamadı.");

                    return tokenResponseModel;
                }
            }
            else
            {
                tokenResponseModel.Result = ResultEnum.Error;
                tokenResponseModel.MessageList.Add("Kullanıcı sistemde bulunamadı.");

                return tokenResponseModel;
            }

        }

        private TokenResponseModel CreateToken()
        {
            JwtSettings jwtSettings = baseConfiguration.Get<JwtSettings>();

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
              issuer: jwtSettings.Issuer,
              audience: jwtSettings.Audience,
              claims: ClaimList,
              expires: DateTime.Now.AddSeconds(jwtSettings.Expires),
              signingCredentials: creds);

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(token);
            var refresh_token = Guid.NewGuid().ToString().Replace("-", "");

            TokenResponseModel tokenResponseModel = new TokenResponseModel
            {
                access_token = encodedJwt,
                expires_in = jwtSettings.Expires,
                refresh_token = refresh_token
            };

            return tokenResponseModel;

        }
        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            JwtSettings jwtSettings = baseConfiguration.Get<JwtSettings>();
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
            var tokenValidationParameters = new TokenValidationParameters
            {
                // The signing key must match!
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,

                // Validate the JWT Issuer (iss) claim
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }

        protected virtual void AddClaimList(LoginResponseModel loginResponseModel)
        {
            ClaimList.Add(new Claim(ClaimTypes.Sid, loginResponseModel.UserID.Value.ToString()));
            ClaimList.Add(new Claim(ClaimTypes.Role, loginResponseModel.Rol.ToString()));
            ClaimList.Add(new Claim(ClaimTypes.NameIdentifier, loginResponseModel.Username.ToString()));
            ClaimList.Add(new Claim(ClaimTypes.Name, loginResponseModel.Username.ToString()));

        }

        protected abstract LoginResponseModel CheckRefreshToken(TokenRefreshRequestModel tokenRefreshRequestModel, long UsersID);
        protected abstract LoginResponseModel CheckUsernamePassword(LoginRequestModel loginRequestModel);
        protected abstract void SaveRefreshToken(TokenResponseModel tokenResponseModel, long UsersID);

        protected abstract List<TokenResponseAdditionalDataModel> AdditionalData(LoginRequestModel request);
    }
}
