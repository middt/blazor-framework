namespace Middt.Framework.Common.Model.Data;

public class BaseSearchRequestModel<T> : BaseRequestDataModel<T>
    where T : new()
{
    public int CurrentPage { get; set; } = 1;

    /// <summary>Page size. Zero or negative returns every matching row.</summary>
    public int RequestItemSize { get; set; } = 10;

    public string? SortProperty { get; set; }

    /// <summary>
    /// <c>true</c> sorts ascending, <c>false</c> sorts descending
    /// (kept for wire compatibility with earlier framework versions).
    /// </summary>
    public bool SortReverse { get; set; }
}
