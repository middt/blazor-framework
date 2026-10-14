using System.Text.Json.Serialization;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Common.Model.Data;

/// <summary>
/// Response envelope shared by the API and its clients (result pattern).
/// </summary>
public class BaseResponseModel
{
    public List<string> MessageList { get; set; } = [];

    public ResultEnum Result { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Result == ResultEnum.Success;

    [JsonIgnore]
    public string ErrorText => string.Join(" ", MessageList);

    public static BaseResponseModel Success(params string[] messages) =>
        new() { Result = ResultEnum.Success, MessageList = [.. messages] };

    public static BaseResponseModel Failure(params string[] messages) =>
        new() { Result = ResultEnum.Error, MessageList = [.. messages] };
}
