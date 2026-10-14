using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Common.Model.Data;

public class BaseResponseDataModel<T> : BaseResponseModel
{
    public int Count { get; set; }

    public T? Data { get; set; }

    public static BaseResponseDataModel<T> Success(T? data, int count = 0) =>
        new() { Result = ResultEnum.Success, Data = data, Count = count };

    public static new BaseResponseDataModel<T> Failure(params string[] messages) =>
        new() { Result = ResultEnum.Error, MessageList = [.. messages] };
}
