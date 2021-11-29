using System;

namespace Suges.Framework.Common.Database.Attributes
{
    public enum DateSearchType
    {
        GreatThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class QueryDateAttribute : Attribute
    {
        public DateSearchType SearchType { get; set; }
        public string PropertyName { get; set; }
        public QueryDateAttribute(DateSearchType searchType)
        {
            SearchType = searchType;
        }
        public QueryDateAttribute(DateSearchType searchType, string propertyName)
        {
            SearchType = searchType;
            PropertyName = propertyName;
        }
    }
}
