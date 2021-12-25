namespace Middt.Framework.Common.Model.Data
{
    public class BaseResponseDataModel<T> : BaseResponseModel
    {
        public int Count { get; set; }
        public T Data { get; set; }
    }
}
