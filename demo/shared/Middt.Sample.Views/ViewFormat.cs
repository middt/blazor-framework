using System.Globalization;

namespace Middt.Sample.Views;

internal static class ViewFormat
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Money(decimal? value) => value is { } v ? v.ToString("N2", Turkish) + " ₺" : "-";

    public static string Date(DateTimeOffset? value) => value is { } v ? v.UtcDateTime.ToString("dd.MM.yyyy HH:mm", Turkish) + " UTC" : "-";

    /// <summary>Calendar date (e.g. a birth date) without time / time zone.</summary>
    public static string DateOnly(DateTimeOffset? value) => value is { } v ? v.UtcDateTime.ToString("dd.MM.yyyy", Turkish) : "-";

    /// <summary>Masks an identity number: first 3 and last 2 digits stay visible (<c>123******01</c>).</summary>
    public static string MaskIdentity(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        return value.Length <= 5 ? new string('*', value.Length) : string.Concat(value.AsSpan(0, 3), new string('*', value.Length - 5), value.AsSpan(value.Length - 2));
    }

    public static string ScoreClass(decimal? score) => score switch
    {
        null => "sv-badge",
        >= 70 => "sv-badge sv-good",
        >= 50 => "sv-badge sv-warn",
        _ => "sv-badge sv-bad",
    };

    /// <summary>Which review the order is in (v2 routes to finance, manager or express review).</summary>
    public static string? ReviewLevel(string state) => state switch
    {
        "finance-review" => "Finans onayı: kurumsal müşteri, 20.000 ₺ üzeri tutar",
        "manager-review" => "Yönetici onayı: 5.000 ₺ üzeri tutar",
        "express-review" => "Ekspres teslimat onayı: hızlı teslimat talebi",
        "escalated" => "Eskale: süresinde karar verilmedi",
        _ => null,
    };
}
