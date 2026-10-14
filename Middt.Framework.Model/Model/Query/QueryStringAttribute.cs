namespace Middt.Framework.Common.Database.Attributes;

public enum StringSearchType
{
    StartsWith,
    Contains,
    EndsWith,
    Equal,
}

/// <summary>Controls how a string property of a search model is translated into a filter (default: Contains).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class QueryStringAttribute(StringSearchType searchType) : Attribute
{
    public StringSearchType SearchType { get; } = searchType;
}
