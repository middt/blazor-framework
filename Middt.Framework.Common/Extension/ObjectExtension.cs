namespace Middt.Framework.Common.Extension;

public static class ObjectExtension
{
    public static bool IsPrimitiveType(this Type type) =>
        type == typeof(object) || Type.GetTypeCode(type) != TypeCode.Object;
}
