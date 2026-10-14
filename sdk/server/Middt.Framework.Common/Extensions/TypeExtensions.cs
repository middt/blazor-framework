namespace Middt.Framework.Common.Extensions;

public static class TypeExtensions
{
    public static bool IsPrimitiveType(this Type type) =>
        type == typeof(object) || Type.GetTypeCode(type) != TypeCode.Object;
}
