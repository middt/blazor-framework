using System;


public static class ObjectExtension
{
    public static bool IsPrimitiveType(this Type type)
    {
        return (type == typeof(object) || Type.GetTypeCode(type) != TypeCode.Object);
    }

}
