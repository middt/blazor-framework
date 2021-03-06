using Middt.Framework.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Middt.Framework.Common.Helper
{
    public static class EnumerationHelper
    {
        public static string GetDescription<TEnum>(this TEnum value)
        {
            var fi = value.GetType().GetField(value.ToString());

            if (fi != null)
            {
                var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attributes.Length > 0)
                {
                    return attributes[0].Description;
                }
            }

            return value.ToString();
        }
        public static List<TextValueItem> GetItems<TEnum>(this TEnum value)
        {
            return Enum.GetValues(value.GetType())
                       .Cast<Enum>()
                       .Select(e => new TextValueItem { Value = e.GetHashCode(), Text = e.GetDescription() })
                       .ToList();
        }
    }
}
