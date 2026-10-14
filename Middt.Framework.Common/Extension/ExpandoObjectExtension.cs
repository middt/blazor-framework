using System.Dynamic;

namespace Middt.Framework.Common.Extension;


public static class ExpandoObjectExtension
{
    public static void Add(this ExpandoObject expandoObject, string name, object? value) =>
        ((IDictionary<string, object?>)expandoObject).Add(name, value);

    public static IDictionary<string, object?> ToDictionary(this ExpandoObject expandoObject) => expandoObject;
}
