namespace Middt.Framework.Model.Authentication
{
    public class TokenRefreshRequestModel : BaseTokenRequestModel
    {
        public string refresh_token { get; set; }
        public string token { get; set; }
    }
}
