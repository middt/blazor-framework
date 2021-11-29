using Suges.Framework.Common.Model.Data;

namespace Suges.Framework.Model.Authentication
{
    public class BaseTokenRequestModel : BaseResponseModel
    {
        public string grant_type { get; set; }
        public string client_id { get; set; }
        public string client_secret { get; set; }
    }
}
