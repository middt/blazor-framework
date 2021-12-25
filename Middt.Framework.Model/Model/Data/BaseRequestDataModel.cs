namespace Middt.Framework.Common.Model.Data
{
    public class BaseRequestDataModel<T> : BaseRequestModel
        where T : new()
    {
        public T RequestModel { get; set; }

        public BaseRequestDataModel()
        {
            RequestModel = new T();
        }
    }
}
