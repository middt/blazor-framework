using System;

namespace Middt.Framework.Common.Model.Data
{
    public class BaseRequestModel : ICloneable
    {
        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}