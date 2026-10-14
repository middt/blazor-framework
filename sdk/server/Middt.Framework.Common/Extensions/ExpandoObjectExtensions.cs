using System.Dynamic;

namespace Middt.Framework.Common.Extensions;

public static class ExpandoObjectExtensions
{
    public static void Add(this ExpandoObject expandoObject, string name, object? value) =>
        ((IDictionary<string, object?>)expandoObject).Add(name, value);

    public static IDictionary<string, object?> ToDictionary(this ExpandoObject expandoObject) => expandoObject;
}
