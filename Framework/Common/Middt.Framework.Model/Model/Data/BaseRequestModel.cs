using System;
using System.Collections.Generic;
using System.Dynamic;

namespace Middt.Framework.Common.Model.Data
{
    //public class CustomDynamicObject : DynamicObject
    //{
    //    public Dictionary<string, object> properties = new Dictionary<string, object>();

    //    public override bool TryGetMember(GetMemberBinder binder, out object result)
    //    {
    //        if (properties.ContainsKey(binder.Name))
    //        {
    //            result = properties[binder.Name];
    //            return true;
    //        }
    //        else
    //        {
    //            result = "error";
    //            return false;
    //        }
    //    }

    //    public override bool TrySetMember(SetMemberBinder binder, object value)
    //    {
    //        properties[binder.Name] = value;
    //        return true;
    //    }

    //    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    //    {
    //        dynamic method = properties[binder.Name];
    //        result = method(args[0].ToString(), args[1].ToString());
    //        return true;
    //    }

    //    public override IEnumerable<string> GetDynamicMemberNames()
    //    {
    //        return properties.Keys;
    //    }
    //}//CustomDynamicObject, ICloneable
    public class BaseRequestModel : ICloneable 
    {
        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
