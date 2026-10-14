using System.Globalization;
using System.Xml;

namespace Middt.Framework.Workflow.Expressions;

public static class Durations
{
    /// <summary>Parses ISO-8601 (<c>PT15M</c>, <c>P2D</c>) or .NET TimeSpan (<c>00:15:00</c>) durations.</summary>
    public static bool TryParse(string? value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith('P') || value.StartsWith('-'))
        {
            try
            {
                duration = XmlConvert.ToTimeSpan(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration);
    }

    public static TimeSpan Parse(string value) =>
        TryParse(value, out var duration) ? duration : throw new FormatException($"'{value}' is not a valid duration (use ISO-8601 like PT15M or 00:15:00).");
}
