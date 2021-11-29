using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Middt.Framework.Common.Validation
{
    public class ArrayMaxLengthAttribute : ValidationAttribute
    {
        readonly int length;

        public ArrayMaxLengthAttribute(int length)
        {
            this.length = length;
        }

        public override bool IsValid(object value)
        {
            if (value is ICollection == false)
            {
                return false;
            }
            return ((ICollection)value).Count < length;
        }
    }
}
