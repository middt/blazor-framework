using System;
using System.ComponentModel.DataAnnotations;

namespace Middt.Template.Common.Service
{
    public class CustomTable1RemoteValidationAttribute : ValidationAttribute
    {
        public CustomTable1RemoteValidationAttribute(Func<string> errorMessageAccessor) : base(errorMessageAccessor)
        {

        }

        public CustomTable1RemoteValidationAttribute(string errorMessage) : base(errorMessage)
        {

        }

        public CustomTable1RemoteValidationAttribute() : base()
        {

        }

        public override bool IsValid(object value)
        {
            //return base.IsValid(value);
            return true;
        }
    }
}
