namespace Middt.Framework.Common.Database.Attributes;

public enum IntSearchType
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
}

/// <summary>Controls how a numeric property of a search model is translated into a filter.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class QueryIntAttribute(IntSearchType searchType) : Attribute
{
    public IntSearchType SearchType { get; } = searchType;
}
