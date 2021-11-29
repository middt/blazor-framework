using System;

namespace Middt.Framework.Common.Database.Attributes
{
    public enum IntSearchType
    {
        GreatThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class QueryIntAttribute : Attribute
    {
        public IntSearchType SearchType { get; set; }
        public QueryIntAttribute(IntSearchType searchType)
        {
            SearchType = searchType;
        }
    }
}
