namespace Middt.Framework.Common.Database.Attributes;

public enum DateSearchType
{
    GreatThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
}

/// <summary>
/// Controls how a <see cref="DateTime"/> property of a search model is translated into a filter.
/// <see cref="PropertyName"/> allows a "range" property (e.g. OrderStartDate) to target another column (OrderDate).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class QueryDateAttribute(DateSearchType searchType, string? propertyName = null) : Attribute
{
    public DateSearchType SearchType { get; } = searchType;
    public string? PropertyName { get; } = propertyName;
}
