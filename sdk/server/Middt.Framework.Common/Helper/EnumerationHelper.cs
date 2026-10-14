using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Middt.Framework.Model;

namespace Middt.Framework.Common.Helper;

public static class EnumerationHelper
{
    public static string GetDescription<TEnum>(this TEnum value)
        where TEnum : struct, Enum =>
        GetDescription((Enum)value);

    public static string GetDescription(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description
        ?? value.ToString();

    public static List<TextValueItem> GetItems<TEnum>(this TEnum _)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(e => new TextValueItem { Value = Convert.ToInt32(e, CultureInfo.InvariantCulture), Text = e.GetDescription() })
            .ToList();
}
