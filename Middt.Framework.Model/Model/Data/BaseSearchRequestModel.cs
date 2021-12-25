namespace Middt.Framework.Common.Model.Data
{
    public class BaseSearchRequestModel<T> : BaseRequestDataModel<T>
        where T : new()
    {
        public int CurrentPage { get; set; }
        public int RequestItemSize { get; set; }
        public string SortProperty { get; set; }
        public bool SortReverse { get; set; }

        public BaseSearchRequestModel() : base()
        {
            CurrentPage = 1;
            RequestItemSize = 10;
        }
    }
}
