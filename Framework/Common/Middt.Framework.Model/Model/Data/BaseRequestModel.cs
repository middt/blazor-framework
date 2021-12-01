using System;
using System.Dynamic;

namespace Middt.Framework.Common.Model.Data
{
    public class BaseRequestModel : DynamicObject, ICloneable
    {
        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
