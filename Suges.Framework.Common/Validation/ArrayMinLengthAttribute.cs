using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Suges.Framework.Common.Validation
{
    public class ArrayMinLengthAttribute : ValidationAttribute
    {
        readonly int length;

        public ArrayMinLengthAttribute(int length)
        {
            this.length = length;
        }
        public override bool IsValid(object value)
        {
            if (value is ICollection == false)
            {
                return false;
            }
            return ((ICollection)value).Count > length;
        }
    }
}
