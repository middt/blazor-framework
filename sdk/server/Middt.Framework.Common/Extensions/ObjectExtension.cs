namespace Middt.Framework.Common.Extension;

[Obsolete("Use Middt.Framework.Common.Extensions.TypeExtensions instead.")]
public static class ObjectExtension
{
    public static bool IsPrimitiveType(this Type type) =>
        Middt.Framework.Common.Extensions.TypeExtensions.IsPrimitiveType(type);
}
