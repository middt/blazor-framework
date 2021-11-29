using Suges.Framework.Model.Model.Enumerations;
using System.Collections.Generic;

namespace Suges.Framework.Common.Model.Data
{
    public class BaseResponseModel
    {
        public List<string> MessageList;
        public BaseResponseModel()
        {
            MessageList = new List<string>();
        }

        public string ErrorText
        {
            get
            {
                return string.Join(" ", MessageList);
            }
        }
        public ResultEnum Result { get; set; }
        // public string Message { get; set; }
    }
}
