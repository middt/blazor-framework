using Middt.Framework.Common.Model.Data;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;

namespace Middt.Framework.Model.Authentication
{
    public class TokenResponseModel : BaseResponseModel
    {
        [SwaggerSchema("Token Deðeri")]
        public string access_token { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        public string Username { get; set; }
        public List<TokenResponseAdditionalDataModel> listTokenResponseAdditionalDataModel { get; set; }

    }

    public class SaveTokenResponseModel : TokenResponseModel
    {
        public SaveTokenResponseModel(TokenResponseModel tokenResponseModel)
        {
            if (tokenResponseModel != null)
            {
                access_token = tokenResponseModel.access_token;
                expires_in = tokenResponseModel.expires_in;
                refresh_token = tokenResponseModel.refresh_token;
                Username = tokenResponseModel.Username;
                listTokenResponseAdditionalDataModel = tokenResponseModel.listTokenResponseAdditionalDataModel;
            }
        }
        public DateTime? expire_datetime { get; set; }

    }
}
