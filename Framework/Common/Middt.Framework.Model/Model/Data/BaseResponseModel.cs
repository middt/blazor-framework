using Middt.Framework.Model.Model.Enumerations;
using System.Collections.Generic;

namespace Middt.Framework.Common.Model.Data
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
