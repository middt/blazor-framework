using QRCoder;

namespace Middt.Identity.Ui.Components.Account.Views;

/// <summary>QR code of an <c>otpauth://</c> URI as inline SVG (no image request, no external service).</summary>
public static class QrCodeSvg
{
    /// <summary>SVG markup for <paramref name="otpauthUri"/>, or <c>null</c> when it is not an <c>otpauth://</c> URI.</summary>
    public static string? Create(string? otpauthUri)
    {
        if (string.IsNullOrEmpty(otpauthUri) || !otpauthUri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(5, "#000000", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    /// <summary>A shared key in groups of four characters (easier to type).</summary>
    public static string FormatKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var compact = key.Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.Join(' ', compact.Chunk(4).Select(c => new string(c))).ToLowerInvariant();
    }
}
