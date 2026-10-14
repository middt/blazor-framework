using System.Dynamic;

namespace Middt.Framework.Common.Extension;

[Obsolete("Use Middt.Framework.Common.Extensions.ExpandoObjectExtensions instead.")]
public static class ExpandoObjectExtension
{
    public static void Add(this ExpandoObject expandoObject, string name, object? value) =>
        Middt.Framework.Common.Extensions.ExpandoObjectExtensions.Add(expandoObject, name, value);

    public static IDictionary<string, object?> ToDictionary(this ExpandoObject expandoObject) =>
        Middt.Framework.Common.Extensions.ExpandoObjectExtensions.ToDictionary(expandoObject);
}
