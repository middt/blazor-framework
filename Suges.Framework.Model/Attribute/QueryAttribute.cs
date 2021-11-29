using System;

namespace Suges.Framework.Model
{
    public class QueryAttribute : Attribute
    {
        public string PropertyName { get; set; }

        public OperatorType Operator { get; set; }
    }

    public enum OperatorType
    {
        Equal = 0,
        NotEqual = 1,
        GreaterThen = 2,
        GreaterThenEqual = 3,
        LessThan = 4,
        LessThanEqual = 5
    }
}
