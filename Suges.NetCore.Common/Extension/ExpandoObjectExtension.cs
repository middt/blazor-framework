using System;
using System.Collections.Generic;
using System.Dynamic;

public static class ExpandoObjectExtension
{
    public static void Add(this ExpandoObject expandoObject, string name, object value)
    {
        var x = expandoObject as IDictionary<string, Object>;
        x.Add(name, value);
    }

    public static IDictionary<string, Object> ToDictionary(this ExpandoObject expandoObject)
    {
        return expandoObject as IDictionary<string, Object>;
    }

}