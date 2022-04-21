using System;

namespace Middt.Framework.Common.Database.Attributes
{
    public enum StringSearchType
    {
        StartsWith,
        Contains,
        EndsWith,
        Any

    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class QueryStringAttribute : Attribute
    {
        public StringSearchType SearchType { get; set; }
        public QueryStringAttribute(StringSearchType searchType)
        {
            SearchType = searchType;
        }
    }
}
